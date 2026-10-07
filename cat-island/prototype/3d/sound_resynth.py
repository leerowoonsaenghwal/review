"""Cat sounds at game quality: harmonic re-synthesis of the recordings (no noise carried over).

    python3 sound_resynth.py SRC_DIR OUT_DIR [--plot sheet.png]

The recordings (assets/sounds/cat_src, sources in SOURCES.md) are home recordings: 8-16 dB above their background
noise, and most are band-limited to about 4-5 kHz (muffled). Spectral noise subtraction on audio like that leaves
'watery' artifacts, so instead each recording is used as a blueprint:
  1. f0 track (the cat's own rise and fall of pitch) by harmonic-sum on the STFT
  2. each harmonic's amplitude over time, read at h*f0 (only where the recording is voiced)
  3. above the recording's band edge, harmonics are continued along the voice's own spectral slope (no more muffle)
  4. re-synthesised as clean sines with smoothed amplitudes, plus a little breath (noise shaped like the voice,
     -26 dB), short fades, the same loudness for every file (RMS), peak under 0.45 (CatAudio.Peak)
Purr: rebuilt from the recording's pulse rhythm (~25 Hz) and spectral envelope: low filtered pulses, no hiss.
Everything runs locally (numpy / scipy). Writes 44.1 kHz mono 16-bit wav.
"""
import os, sys, glob
import numpy as np
from scipy.io import wavfile
from scipy.signal import stft, butter, sosfiltfilt, resample_poly

SR = 44100
N_FFT, HOP = 2048, 256
PEAK, TARGET_RMS_DB = .45, -17.0


def load(p):
    sr, x = wavfile.read(p)
    x = x.astype(np.float64)
    if x.ndim > 1: x = x.mean(1)
    if np.abs(x).max() > 1.5: x /= 32768.0
    if sr != SR: x = resample_poly(x, SR, sr)
    return x


def smooth(a, n):
    if n <= 1: return a
    k = np.hanning(n * 2 + 1); k /= k.sum()
    pad = np.pad(a, (n, n), mode='edge')
    return np.convolve(pad, k, mode='valid')


def band_edge(mag, f):
    """highest frequency the recording really carries (95th-percentile frame spectrum falls 45 dB below its peak)"""
    spec = 20 * np.log10(np.percentile(mag, 95, axis=1) + 1e-12)
    ok = np.where(spec > spec.max() - 45)[0]
    return float(f[ok.max()]) if len(ok) else 4000.0


def track_f0(mag, f, lo=300., hi=1600.):
    """harmonic-sum f0 per frame (sum of log magnitude at the first 6 harmonics), then median-smoothed"""
    cands = np.arange(lo, hi, 4.0)
    L = np.log(mag + 1e-9)
    df = f[1] - f[0]
    score = np.zeros((len(cands), mag.shape[1]))
    for h in range(1, 7):
        idx = np.clip(np.round(cands * h / df).astype(int), 0, len(f) - 1)
        score += L[idx] * (1.0 / h ** .3)
    # harmonicity: how far the best candidate stands above the median candidate
    harm = score.max(0) - np.median(score, 0)
    # best continuous path (dynamic programming): a cat's pitch glides, it does not jump between frames, so a jump
    # costs in proportion to its size in octaves (a frame is 6 ms: an octave jump costs far more than any score gain)
    z = (score - score.mean(0)) / (score.std(0) + 1e-9)
    lc = np.log2(cands); jump = 140.0 * np.abs(lc[:, None] - lc[None, :])
    acc = z[:, 0].copy(); back = np.zeros(score.shape, int)
    for i in range(1, score.shape[1]):
        tot = acc[None, :] - jump            # rows: this frame's candidate, cols: previous
        back[:, i] = tot.argmax(1); acc = tot.max(1) + z[:, i]
    path = np.zeros(score.shape[1], int); path[-1] = acc.argmax()
    for i in range(score.shape[1] - 1, 0, -1): path[i - 1] = back[path[i], i]
    return cands[path], harm


def resynth_voice(x, seed=0):
    f, t, Z = stft(x, SR, nperseg=N_FFT, noverlap=N_FFT - HOP, boundary=None, padded=False)
    mag = np.abs(Z) * 2 / N_FFT * 2
    edge = min(band_edge(mag, f), 9000.)
    f0, harm = track_f0(mag, f)
    frame_rms = np.sqrt((mag ** 2).sum(0))
    frame_db = 20 * np.log10(frame_rms + 1e-12)
    noise_db = np.percentile(frame_db, 10)
    voiced = (frame_db > noise_db + 6) & (harm > np.percentile(harm, 30))
    # voicing as a soft 0..1 gate (frames well above the noise), closed small gaps
    v = np.clip((frame_db - (noise_db + 4)) / 10, 0, 1) * voiced
    # one meow is one sound: gaps shorter than ~0.12 s inside it are closed (the gate opened and shut on noise)
    g = int(.12 * SR / HOP); on = v > .05; idx = np.where(on)[0]
    for a, b in zip(idx[:-1], idx[1:]):
        if 1 < b - a <= g: v[a:b] = np.maximum(v[a:b], np.linspace(v[a], v[b], b - a))
    # pieces shorter than ~60 ms standing alone are clicks, not voice
    on = v > .05; i = 0; mn = int(.06 * SR / HOP)
    while i < len(on):
        if on[i]:
            j = i
            while j < len(on) and on[j]: j += 1
            if j - i < mn: v[i:j] = 0
            i = j
        else: i += 1
    v = smooth(v, 3)
    df = f[1] - f[0]
    H = int(min(40, 9500 // max(200, f0.min())))
    amps = np.zeros((H, len(t)))
    noise_mag = np.percentile(mag, 15, axis=1)   # the background per bin
    for h in range(1, H + 1):
        fh = f0 * h
        b = np.clip(np.round(fh / df).astype(int), 1, len(f) - 2)
        # parabolic peak pick within +-2 bins, minus the background at that bin
        a = np.max(np.stack([mag[np.clip(b + d, 0, len(f) - 1), np.arange(len(t))] for d in (-2, -1, 0, 1, 2)]), 0)
        a = np.maximum(a - noise_mag[b] * 1.5, 0)
        a[fh >= edge * .97] = 0
        amps[h - 1] = a
    # a harmonic missing for a frame or two inside the voice (noise ate it) is filled from its neighbours
    for h in range(H):
        a = amps[h]; z = np.where((a == 0) & (v > .05))[0]
        if len(z) and (a > 0).sum() > 2: amps[h, z] = np.interp(z, np.where(a > 0)[0], a[a > 0])
    # continue the harmonics above the band edge along the voice's slope (dB per octave from the top ones)
    for i in range(len(t)):
        hs = np.where(amps[:, i] > 0)[0]
        if len(hs) < 3: continue
        top = hs[-4:]
        k = np.log2(top + 1.0); d = 20 * np.log10(amps[top, i] + 1e-12)
        slope = np.polyfit(k, d, 1)[0] if len(top) >= 2 else -9.0
        slope = float(np.clip(slope, -14., -6.))
        last = hs[-1]
        for h in range(last + 1, H):
            if f0[i] * (h + 1) > 9500: break
            amps[h, i] = amps[last, i] * 10 ** (slope * np.log2((h + 1) / (last + 1)) / 20)
    # time-smooth amplitudes (no warble), apply voicing
    for h in range(H): amps[h] = smooth(amps[h], 2) * v
    f0s = smooth(f0, 2)
    # sample-rate synthesis
    n = len(x)
    ts = np.arange(n) / SR
    tf = t
    f0n = np.interp(ts, tf, f0s)
    phase = 2 * np.pi * np.cumsum(f0n) / SR
    rng = np.random.default_rng(seed)
    y = np.zeros(n)
    for h in range(H):
        a = np.interp(ts, tf, amps[h])
        if a.max() <= 0: continue
        y += a * np.sin((h + 1) * phase + rng.uniform(0, 2 * np.pi))
    # breath: white noise band-passed around the voice's strongest region, following the voice envelope
    env = np.interp(ts, tf, v * frame_rms / (frame_rms.max() + 1e-12))
    sos = butter(2, [1200, 5000], btype='band', fs=SR, output='sos')
    breath = sosfiltfilt(sos, rng.standard_normal(n)) * env
    y = y / (np.abs(y).max() + 1e-12) + breath / (np.abs(breath).max() + 1e-12) * 10 ** (-42 / 20)
    return finish(y), edge, float(np.median(f0[voiced])) if voiced.any() else 0.0


def resynth_purr(x, seed=1):
    """pulse rhythm and spectral envelope from the recording; low filtered pulses (no hiss)"""
    rng = np.random.default_rng(seed)
    sos_env = butter(2, 60, btype='low', fs=SR, output='sos')
    env = sosfiltfilt(sos_env, np.abs(x))
    env = np.maximum(env, 0); env /= env.max() + 1e-12
    # spectral shape of the purr body (the recording below 1.2 kHz)
    f, t, Z = stft(x, SR, nperseg=4096, noverlap=3072)
    shape = np.median(np.abs(Z), axis=1); shape[f > 1200] *= np.exp(-(f[f > 1200] - 1200) / 300)
    noise = rng.standard_normal(len(x))
    Nn = np.fft.rfft(noise); fn = np.fft.rfftfreq(len(noise), 1 / SR)
    body = np.fft.irfft(Nn * np.interp(fn, f, shape / shape.max()), len(noise))
    # pulses: the recording's own amplitude rhythm, sharpened a little so each 'r' is distinct
    pulses = env ** 1.6
    y = body / (np.abs(body).max() + 1e-12) * pulses
    sos = butter(4, [35, 900], btype='band', fs=SR, output='sos')
    y = sosfiltfilt(sos, y)
    # seamless loop: cross-fade the last 80 ms into the start
    m = int(.08 * SR); w = np.linspace(0, 1, m)
    y[:m] = y[:m] * w + y[-m:] * (1 - w); y = y[:-m]
    return finish(y, fade=False, target_db=-20.0)


def finish(y, fade=True, target_db=TARGET_RMS_DB):
    sos = butter(2, 120, btype='high', fs=SR, output='sos')
    y = sosfiltfilt(sos, y)
    if fade:
        a, b = int(.006 * SR), int(.04 * SR)
        y[:a] *= np.linspace(0, 1, a) ** 2; y[-b:] *= np.linspace(1, 0, b) ** 2
    # loudness: RMS of the loud part to target, then peak under PEAK
    f = int(.02 * SR); fr = np.sqrt((y[:len(y) // f * f].reshape(-1, f) ** 2).mean(1))
    loud = np.percentile(fr[fr > 0], 80) if (fr > 0).any() else 1
    y *= 10 ** (target_db / 20) / (loud + 1e-12)
    pk = np.abs(y).max()
    if pk > PEAK: y *= PEAK / pk
    return y


def main():
    src, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    for i, p in enumerate(sorted(glob.glob(os.path.join(src, '*.wav')))):
        name = os.path.basename(p); x = load(p)
        if name.startswith('purr'):
            y = resynth_purr(x); info = 'purr'
        else:
            y, edge, f0 = resynth_voice(x, seed=i); info = f'band edge {edge:.0f} Hz, f0 ~{f0:.0f} Hz'
        wavfile.write(os.path.join(out, name), SR, (np.clip(y, -1, 1) * 32767).astype(np.int16))
        print(f'{name:16s} {len(y) / SR:5.2f}s  {info}')


if __name__ == '__main__':
    main()
