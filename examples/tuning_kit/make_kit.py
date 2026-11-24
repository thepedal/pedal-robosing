#!/usr/bin/env python3
"""Writes the RoboSing tuning kit: an original eight-line test tune as a MIDI file,
and its words. Each line stresses one thing the voice has to get right."""
import struct

BPM, DIV = 100, 480
LINES = [   # (what it tests, words, [(beat, length in beats, MIDI note)])
 ("long open vowels",        "He saw two more.",
    [(0,1.25,60),(1.5,1.25,64),(3,1.25,67),(4.5,1.5,65)]),
 ("stop consonants",         "Take a big dip, pat the dog.",
    [(8,.5,67),(8.5,.5,65),(9,.5,64),(9.5,1,62),(11,.5,64),(11.5,.5,65),(12,1.5,67)]),
 ("hissing sounds",          "She sells fresh fish, thin and sharp.",
    [(16,.5,64),(16.5,.75,65),(17.5,.5,67),(18,1,69),(19.5,.75,67),(20.25,.25,65),(20.5,1.5,64)]),
 ("nasals and l",            "Lean on me, my lonely moon.",
    [(24,1,60),(25,.5,62),(25.5,1.5,64),(27.5,.5,62),(28,.75,64),(28.75,.25,62),(29,2,60)]),
 ("one syllable, many notes","Oh, hold on.",
    [(32,.5,67),(32.5,.5,69),(33,.5,67),(33.5,1,64),(35,.5,65),(36,.5,64),(36.5,1.5,62)]),
 ("fast syllables",          "Tick tock, pitter patter, little clock.",
    [(40,.25,64),(40.5,.25,60),(41,.25,64),(41.25,.25,64),(41.5,.25,65),(41.75,.25,65),(42,.25,67),(42.25,.25,67),(42.5,1,72)]),
 ("gliding vowels",          "I fly high, now, boy.",
    [(48,1,60),(49,1.5,64),(50.5,2,67),(53,1,65),(54.5,1.5,60)]),
 ("an octave, low to high",  "La la la la la la la la.",
    [(56+i*.75,.7,p) for i,p in enumerate([60,62,64,65,67,69,71,72])]),
]

def var(n):
    out=[n&0x7F]; n>>=7
    while n: out.append(0x80|(n&0x7F)); n>>=7
    return bytes(reversed(out))
def track(events):
    body=b""; now=0
    for t,data in sorted(events,key=lambda e:(e[0],e[1][0]&0xF0!=0x80)):
        body+=var(t-now)+data; now=t
    body+=var(0)+b"\xFF\x2F\x00"
    return b"MTrk"+struct.pack(">I",len(body))+body
us=round(60_000_000/BPM)
tempo=[(0,b"\xFF\x03"+bytes([5])+b"Tempo"),(0,b"\xFF\x51\x03"+us.to_bytes(3,"big"))]
name=b"Voice"
voice=[(0,b"\xFF\x03"+bytes([len(name)])+name)]
for _,_,notes in LINES:
    for beat,length,pitch in notes:
        a=round(beat*DIV); b=round((beat+length)*DIV)
        voice.append((a,bytes([0x90,pitch,100]))); voice.append((b,bytes([0x80,pitch,0])))
open("robosing_tuning_kit.mid","wb").write(b"MThd"+struct.pack(">IHHH",6,1,2,DIV)+track(tempo)+track(voice))
open("robosing_tuning_kit_words.txt","w").write("\n".join(w for _,w,_ in LINES)+"\n")
for i,(what,w,n) in enumerate(LINES,1): print(f"{i}. {w:42s} {len(n):2d} notes  ({what})")
