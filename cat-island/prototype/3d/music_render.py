"""Background music at game quality, made in code (no samples, no services): day and night pieces.

    python3 music_render.py OUT_DIR          -> OUT_DIR/bgm_day.ogg, bgm_night.ogg (44.1 kHz stereo, seamless loops)

Day: 84 BPM, C major, intro + A B A' C A'' (+ the intro again as the loop seam), ~2.4 min before it repeats.
     Music box melody, marimba arpeggios, soft upright-ish bass, warm pad. Night: 64 BPM, F major, music box and pad
     only, lower and sparser, more room. Melodies are pentatonic (no note clashes with the chords), built from a
     2-bar motif that is repeated, varied and answered, ending each phrase on a chord tone.
Instruments are modelled, not plain sines: the music box tine has the inharmonic partials of a cantilever comb tooth
(1, 6.27, 17.55) whose upper partials die fast; the marimba bar has its tuned partials (1, 3.93, 9.2) and a short
mallet click; the pad is detuned saw-like voices through a gentle low-pass with slow swells. Everything goes through
a small stereo room (Schroeder reverb) and a soft limiter. The tail of the last bar is folded onto the start so the
loop has no seam. Deterministic (fixed seeds).
"""
import os, sys
import numpy as np
import soundfile as sf

SR = 44100
rng_master = np.random.default_rng(11)


def hz(m): return 440.0 * 2 ** ((m - 69) / 12)


# ---------------------------------------------------------------- instruments (mono note -> array)
def env_ad(n, a, d, sr=SR):
    t = np.arange(n) / sr
    return np.minimum(1, t / max(a, 1e-4)) * np.exp(-t / d)


def music_box(f, dur, vel, rng):
    n = int((dur + 3.0) * SR); t = np.arange(n) / SR
    det = 1 + rng.normal(0, .0007)
    parts = [(1.0, 1.0, 2.6), (6.27, .22, .35), (17.55, .06, .08), (2.0, .05, 1.2)]   # (ratio, amp, decay s)
    y = np.zeros(n)
    for r, a, dcy in parts:
        fr = f * r * det
        if fr > SR / 2.2: continue
        y += a * np.sin(2 * np.pi * fr * t + rng.uniform(0, 6.28)) * np.exp(-t / dcy)
    y *= np.minimum(1, t / .002)                      # (pluck: almost instant)
    return y * vel


def marimba(f, dur, vel, rng):
    n = int((dur + 1.6) * SR); t = np.arange(n) / SR
    y = (np.sin(2 * np.pi * f * t) * np.exp(-t / .9) + .32 * np.sin(2 * np.pi * f * 3.93 * t) * np.exp(-t / .18)
         + .1 * np.sin(2 * np.pi * f * 9.2 * t) * np.exp(-t / .05))
    click = rng.normal(0, 1, int(.006 * SR)) * np.linspace(1, 0, int(.006 * SR)) ** 2
    click = np.convolve(click, np.ones(12) / 12, mode='same') * .25          # (soft mallet: low-passed)
    y[:len(click)] += click
    y *= np.minimum(1, t / .003)
    return y * vel


def bass(f, dur, vel, rng):
    n = int((dur + .8) * SR); t = np.arange(n) / SR
    y = np.sin(2 * np.pi * f * t) + .35 * np.sin(4 * np.pi * f * t) * np.exp(-t / .4) + .12 * np.sin(6 * np.pi * f * t) * np.exp(-t / .2)
    e = np.minimum(1, t / .012) * np.exp(-t / .9)
    rel = np.clip((dur + .25 - t) / .25, 0, 1)                                # (let go at the end of the note)
    return y * e * rel * vel


def pad(freqs, dur, vel, rng):
    n = int((dur + 1.5) * SR); t = np.arange(n) / SR
    y = np.zeros(n)
    for f in freqs:
        for d in (-.004, 0, .004):                                           # (three detuned voices: chorus)
            ph = rng.uniform(0, 6.28)
            for h, a in ((1, 1), (2, .3), (3, .12), (4, .05)):
                y += a * np.sin(2 * np.pi * f * (1 + d) * h * t + ph * h)
    swell = np.minimum(1, t / .8) * np.clip((dur + 1.2 - t) / 1.2, 0, 1)
    return y * swell * vel / (len(freqs) * 3)


# ---------------------------------------------------------------- composition
PENTA = [0, 2, 4, 7, 9]


def deg_to_midi(deg, base):
    o, i = divmod(deg, 5)
    return base + 12 * o + PENTA[i]


def motif(rng, bars=2):
    rhythms = [[1, 1, 2], [.5, .5, 1, 2], [1, .5, .5, 2], [1.5, .5, 2], [1, 1, 1, 1], [2, 1, 1]]
    out, deg, beat = [], 4 + rng.integers(0, 3), 0.0
    for b in range(bars):
        for ln in rhythms[rng.integers(0, len(rhythms))]:
            deg = int(np.clip(deg + rng.choice([-2, -1, -1, 1, 1, 2, 0]), 2, 9))
            out.append((beat, ln, deg)); beat += ln
    return out


def phrase(rng, m, variant):
    """4 bars = motif + its answer (varied)."""
    notes = [(b, l, d) for b, l, d in m]
    ans = []
    for b, l, d in m:
        dd = d + (1 if variant == 1 else -1 if variant == 2 else 0) if b < 4 else d
        ans.append((b + 8, l, dd))
    # last note of the phrase: long, on a stable degree
    if ans: b, l, d = ans[-1]; ans[-1] = (b, max(l, 2.0), 5 if variant != 2 else 2)
    return notes + ans


def render(bpm, sections, base_mel, layers, seed, length_bars_hint=None):
    rng = np.random.default_rng(seed)
    beat = 60.0 / bpm
    bars = sum(len(ch) for _, ch, _ in sections)
    loop_n = int(round(bars * 4 * beat * SR)); tail = int(4 * SR)
    L = np.zeros(loop_n + tail); R = np.zeros(loop_n + tail)

    def add(sig, t0, pan, gain):
        i0 = int(round(t0 * SR)); n = min(len(sig), len(L) - i0)
        if n <= 0: return
        l = np.cos((pan + 1) * np.pi / 4); r = np.sin((pan + 1) * np.pi / 4)
        L[i0:i0 + n] += sig[:n] * gain * l; R[i0:i0 + n] += sig[:n] * gain * r

    motifs = {k: motif(rng) for k in 'ABC'}
    bar0 = 0
    for kind, chords, var in sections:
        t_sec = bar0 * 4 * beat
        for bi, ch in enumerate(chords):
            t = t_sec + bi * 4 * beat
            if 'pad' in layers: add(pad([hz(m + 12) for m in ch], 4 * beat, .22 if kind != 'I' else .3, rng), t, 0, 1.0)
            if 'bass' in layers and kind != 'I':
                add(bass(hz(ch[0] - 12), 1.8 * beat, .5, rng), t, -.1, 1.0)
                add(bass(hz(ch[0] - 12 + (7 if bi % 2 else 0)), 1.6 * beat, .38, rng), t + 2 * beat, -.1, 1.0)
            if 'marimba' in layers and kind in 'ABC':
                pat = [0, 1, 2, 3, 2, 1, 2, 1] if kind != 'C' else [0, 2, 1, 3, 0, 2, 1, 2]
                for k, p in enumerate(pat):
                    m = ch[p] + 12 if p < 3 else ch[0] + 24
                    add(marimba(hz(m), .5 * beat, .26 if k % 2 == 0 else .18, rng), t + k * .5 * beat, .35, 1.0)
        if kind in 'ABC' and 'melody' in layers:
            key = 'A' if kind == 'A' else kind
            for half in range(0, len(chords), 4):
                ph = phrase(rng, motifs[key], var if half == 0 else (var + 1) % 3)
                for b, ln, d in ph:
                    if b >= 16: continue
                    bar = half + int(b // 4)
                    if bar >= len(chords): continue
                    m = deg_to_midi(d, base_mel)
                    if abs(b - round(b)) < 1e-6 and int(b) % 2 == 0:            # (strong beats: a chord tone)
                        chord = chords[bar]; pcs = [(x % 12) for x in chord]
                        cands = [mm for mm in range(m - 4, m + 5) if mm % 12 in pcs]
                        if cands: m = min(cands, key=lambda mm: abs(mm - m))
                    add(music_box(hz(m), ln * beat, .5, rng), t_sec + b * beat + rng.normal(0, .006), -.25, 1.0)
        if kind == 'I' and 'melody' in layers:                               # (intro: a few bell notes)
            for i, d in enumerate([7, 5, 4, 2]):
                add(music_box(hz(deg_to_midi(d, base_mel)), 2 * beat, .35, rng), t_sec + i * 2 * beat, .2, 1.0)
        bar0 += len(chords)

    # fold the tail onto the start: a seamless loop
    L[:tail] += L[loop_n:]; R[:tail] += R[loop_n:]
    L, R = L[:loop_n], R[:loop_n]
    L, R = reverb(L, R, wet=layers.get('room', .2))
    # gentle low-pass (no harsh highs on phone speakers) and soft limiting
    st = np.stack([L, R], 1)
    st = lowpass(st, 7500)
    st /= np.abs(st).max() + 1e-9
    st = np.tanh(st * 1.2) / np.tanh(1.2) * .5                                # (peak .5: under the cat sounds)
    return st


def lowpass(x, fc):
    X = np.fft.rfft(x, axis=0); f = np.fft.rfftfreq(len(x), 1 / SR)
    g = 1 / np.sqrt(1 + (f / fc) ** 4)
    return np.fft.irfft(X * g[:, None], len(x), axis=0)


def reverb(L, R, wet=.2):
    """small room: 4 parallel combs + 2 allpasses per channel (Schroeder), circular so the loop stays seamless"""
    def comb(x, d, g):
        y = np.zeros_like(x)
        for _ in range(6):                     # (circular: the decay wraps round the loop)
            y = x + g * np.roll(y, d)
        return y
    def allpass(x, d, g):
        y = np.zeros_like(x)
        for _ in range(6):
            y = -g * x + np.roll(x, d) + g * np.roll(y, d)
        return y
    out = []
    for x, off in ((L, 0), (R, 23)):
        s = sum(comb(x, int((dd + off) * SR / 1000), g) for dd, g in ((29.7, .78), (37.1, .76), (41.1, .75), (43.7, .73))) / 4
        for dd, g in ((5.0, .7), (1.7, .7)): s = allpass(s, int(dd * SR / 1000), g)
        out.append(x * (1 - wet) + s * wet)
    return out[0], out[1]


C, Am, F, G, Em, Dm = [48, 52, 55], [45, 48, 52], [41, 45, 48], [43, 47, 50], [40, 43, 47], [38, 41, 45]


def day():
    secs = [('I', [C, F, C, G], 0), ('A', [C, G, Am, F, C, G, F, C], 0), ('B', [F, G, Em, Am, F, G, C, C], 0),
            ('A', [C, G, Am, F, C, G, F, C], 1), ('C', [Am, F, C, G, Am, F, G, G], 0), ('A', [C, G, Am, F, C, G, F, C], 2)]
    return render(84, secs, 72, {'pad': 1, 'bass': 1, 'marimba': 1, 'melody': 1, 'room': .18}, seed=7)


def night():
    up = lambda ch: [x + 5 for x in ch]   # (F major: the same shapes a fourth up)
    secs = [('I', [up(C), up(F), up(C), up(G)], 0), ('A', [up(c) for c in [C, Am, F, G, C, Am, F, C]], 0),
            ('B', [up(c) for c in [F, G, Em, Am, Dm, G, C, C]], 0), ('A', [up(c) for c in [C, Am, F, G, C, Am, F, C]], 2)]
    return render(64, secs, 72, {'pad': 1, 'bass': 1, 'melody': 1, 'room': .3}, seed=19)


if __name__ == '__main__':
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    for name, fn in (('bgm_day', day), ('bgm_night', night)):
        st = fn()
        st = np.ascontiguousarray(st, dtype=np.float32)
        with sf.SoundFile(os.path.join(out, name + '.ogg'), 'w', SR, 2, format='OGG', subtype='VORBIS') as f:   # (in blocks: one big write crashes libsndfile's Vorbis encoder)
            for i in range(0, len(st), 4096): f.write(st[i:i + 4096])
        print(name, f'{len(st) / SR:.1f}s', 'peak', round(float(np.abs(st).max()), 3))
