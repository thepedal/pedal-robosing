#!/usr/bin/env python3
"""Measure a recording of the tuning kit against what was asked for.
usage: analyse.py recording.wav [reference.wav]
With a reference (the harness render), the recording is lined up to it first."""
import sys, numpy as np
from scipy.io import wavfile
from scipy.signal import correlate
import make_kit as K

def load(path):
    sr, x = wavfile.read(path)
    x = x.astype(np.float64)
    if x.ndim > 1: x = x.mean(axis=1)
    peak = np.iinfo(np.int16).max if x.max() > 1.5 or x.min() < -1.5 else 1.0
    if np.abs(x).max() > 40000: peak = 2**31
    return sr, x / peak

def env(x, sr, hop=0.01):
    n = int(sr*hop); m = len(x)//n
    return np.sqrt((x[:m*n].reshape(m, n)**2).mean(axis=1))

def f0(seg, sr, lo=70, hi=1000):
    seg = seg - seg.mean()
    if len(seg) < sr//lo*2 or np.sqrt((seg**2).mean()) < 1e-4: return 0, 0
    seg = seg * np.hanning(len(seg))
    r = correlate(seg, seg, mode='full')[len(seg)-1:]
    r /= r[0] + 1e-12
    a, b = sr//hi, sr//lo
    best = r[a:b].max()
    for lag in range(a+1, b-1):                       # the shortest period nearly as good as the best
        if r[lag] >= 0.9*best and r[lag] >= r[lag-1] and r[lag] >= r[lag+1]:
            y0, y1, y2 = r[lag-1], r[lag], r[lag+1]
            d = 0.5*(y0-y2)/(y0-2*y1+y2+1e-12)
            return sr/(lag+d), r[lag]
    return 0, 0

sr, x = load(sys.argv[1])
offset = 0.0
if len(sys.argv) > 2:
    sr2, ref = load(sys.argv[2])
    e1, e2 = env(x, sr), env(ref, sr2)
    c = correlate(e1 - e1.mean(), e2 - e2.mean(), mode='full')
    offset = (c.argmax() - (len(e2)-1)) * 0.01
    print(f"recording starts {offset:+.2f} s against the reference; length {len(x)/sr:.1f} s vs {len(ref)/sr2:.1f} s; sample rate {sr}")

first = K.LINES[0][2][0][0]
t = lambda beat: offset + (beat-first)*60/K.BPM
print(f"{'line':42s} {'level':>7s} {'hiss':>6s} {'pitch error per note, cents (.. = no clear pitch)'}")
allc = []
for what, words, notes in K.LINES:
    a, b = int(t(notes[0][0])*sr), int(t(notes[-1][0]+notes[-1][1])*sr)
    seg = x[max(0,a):b]
    lvl = 20*np.log10(np.sqrt((seg**2).mean())+1e-9)
    S = np.abs(np.fft.rfft(seg*np.hanning(len(seg))))**2; fr = np.fft.rfftfreq(len(seg), 1/sr)
    hiss = 100*S[fr > 4000].sum()/S.sum()
    cents = []
    for beat, length, midi in notes:
        s0, s1 = int(t(beat+0.35*length)*sr), int(t(beat+0.9*length)*sr)
        f, q = f0(x[max(0,s0):s1], sr)
        want = 440*2**((midi-69)/12)
        if f <= 0 or q < 0.5: cents.append("..")
        else:
            c = 1200*np.log2(f/want)
            c = (c+600) % 1200 - 600                 # an octave slip of the detector is not an error of the voice
            cents.append(f"{c:+.0f}"); allc.append(c)
    print(f"{words:42s} {lvl:6.1f}dB {hiss:5.1f}% {' '.join(cents)}")
if allc: print(f"pitch: {len(allc)} notes measured, median error {np.median(allc):+.0f} cents, worst {max(allc, key=abs):+.0f}")
