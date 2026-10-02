#!/bin/sh
# Downloads what photo.html needs but git doesn't store:
#   models/deeplab_v3.tflite   on-device segmentation (stands in for iOS Vision's subject mask)
#   photos/*.jpg               test photos from Wikimedia Commons (put your own cat in photos/<name>.jpg)
set -e
cd "$(dirname "$0")"
mkdir -p models photos
UA="cat-island-prototype/0.1"
[ -f models/deeplab_v3.tflite ] || curl -sSL -o models/deeplab_v3.tflite \
  https://storage.googleapis.com/mediapipe-models/image_segmenter/deeplab_v3/float32/1/deeplab_v3.tflite
get() { [ -f "photos/$1.jpg" ] || { curl -sSL -A "$UA" -o "photos/$1.jpg" "$2"; sleep 2; }; }
get siamese "https://upload.wikimedia.org/wikipedia/commons/1/16/Siamese_cat_Vaillante.JPG"
get black   "https://upload.wikimedia.org/wikipedia/commons/4/4c/Blackcat-Lilith.jpg"
get russian "https://upload.wikimedia.org/wikipedia/commons/4/4f/Russian_blue_kitten_%28cropped%29.jpg"
get tabby   "https://upload.wikimedia.org/wikipedia/commons/4/4d/Cat_November_2010-1a.jpg"
get persian "https://upload.wikimedia.org/wikipedia/commons/8/81/Persialainen.jpg"
echo done
