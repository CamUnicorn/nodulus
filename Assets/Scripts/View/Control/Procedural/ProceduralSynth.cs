using System;
using UnityEngine;

namespace View.Control.Procedural
{
    public enum SynthWave { Sine, Triangle, Wavetable }

    public static class WaveTable
    {
        private const int Size = 2048; // potencia de 2
        private static readonly float[] Soft = Build();

        // Un ciclo de onda precalculado: fundamental + armónicos decrecientes (timbre suave tipo órgano)
        private static float[] Build()
        {
            var t = new float[Size];
            float max = 0f;
            for (int i = 0; i < Size; i++)
            {
                float ph = (float)i / Size * 2f * Mathf.PI;
                t[i] = Mathf.Sin(ph) + 0.4f * Mathf.Sin(2f * ph)
                     + 0.15f * Mathf.Sin(3f * ph) + 0.06f * Mathf.Sin(4f * ph);
                max = Mathf.Max(max, Mathf.Abs(t[i]));
            }
            for (int i = 0; i < Size; i++) t[i] /= max; // normalizado a ±1
            return t;
        }

        // Lectura con interpolación lineal
        public static float Read(float phaseRad)
        {
            float pos = phaseRad * (Size / (2f * Mathf.PI));
            pos -= Mathf.Floor(pos / Size) * Size;
            int i = (int)pos;
            float fr = pos - i;
            float a = Soft[i & (Size - 1)];
            float b = Soft[(i + 1) & (Size - 1)];
            return a + (b - a) * fr;
        }
    }

    /// <summary>Parámetros de UNA nota/sonido corto. Todo lo audible sale de aquí.</summary>
    public struct SynthPatch
    {
        public SynthWave wave;
        public float freqStart, freqEnd, glideTime;      // pitch envelope (Hz, s)
        public float attack, decay, sustain, hold, release; // ADSR (hold = duración de la compuerta)
        public float amp;
        public float partial2, partial3;                 // síntesis aditiva (2x y 3x la fundamental)
        public float fmRatio, fmIndex;                   // FM ligera (index 0 = sin FM)
        public float noise;                              // mezcla de ruido blanco
        public float cutoff;                             // low-pass de un polo (Hz, 0 = apagado)
        public float lfoRate, lfoDepth;                  // tremolo (LFO sobre amplitud)
        public float delay;                              // segundos antes de sonar
    }

    /// <summary>Una voz del sintetizador. Se renderiza en el hilo de audio.</summary>
    public class SynthVoice
    {
        private const double TwoPi = Math.PI * 2.0;

        public bool Active;
        public long Age;

        private SynthPatch _p;
        private readonly float _sr;
        private double _phase, _modPhase, _lfoPhase;
        private long _delaySamples;
        private float _lp, _lpCoef;
        private uint _rng;

        public SynthVoice(int sampleRate, uint seed)
        {
            _sr = sampleRate;
            _rng = seed | 1u;
        }

        public void Start(SynthPatch p)
        {
            _p = p;
            _phase = 0; _modPhase = 0; _lfoPhase = 0;
            _delaySamples = (long)(p.delay * _sr);
            Age = 0;
            _lp = 0f;
            _lpCoef = p.cutoff > 0f ? 1f - Mathf.Exp(-(float)TwoPi * p.cutoff / _sr) : 1f;
            Active = true;
        }

        private float Gate(float t)
        {
            float a = Mathf.Max(_p.attack, 0.001f);
            if (t < a) return t / a;
            float d = Mathf.Max(_p.decay, 0.001f);
            if (t < a + d) return 1f - (1f - _p.sustain) * ((t - a) / d);
            return _p.sustain;
        }

        private float Osc(float x)
        {
            switch (_p.wave)
            {
                case SynthWave.Triangle: return 0.6366198f * Mathf.Asin(Mathf.Sin(x));
                case SynthWave.Wavetable: return WaveTable.Read(x);
                default: return Mathf.Sin(x);
            }
        }

        private float Noise()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 8388608f - 1f;
        }

        public float Next()
        {
            if (!Active) return 0f;
            if (_delaySamples > 0) { _delaySamples--; return 0f; }

            float t = Age / _sr;
            Age++;

            // --- ADSR + release ---
            float e;
            if (t <= _p.hold) e = Gate(t);
            else
            {
                float r = (t - _p.hold) / Mathf.Max(_p.release, 0.001f);
                if (r >= 1f) { Active = false; return 0f; }
                e = Gate(_p.hold) * (1f - r);
            }

            // --- pitch envelope (glide exponencial) ---
            float f = _p.freqStart;
            if (_p.glideTime > 0f && _p.freqEnd > 0f)
            {
                float g = Mathf.Min(1f, t / _p.glideTime);
                f = _p.freqStart * Mathf.Pow(_p.freqEnd / _p.freqStart, g);
            }

            _phase += TwoPi * f / _sr;
            if (_phase > TwoPi) _phase -= TwoPi;
            float ph = (float)_phase;

            // --- FM ligera ---
            float mod = 0f;
            if (_p.fmIndex > 0f)
            {
                _modPhase += TwoPi * f * _p.fmRatio / _sr;
                if (_modPhase > TwoPi) _modPhase -= TwoPi;
                mod = _p.fmIndex * Mathf.Sin((float)_modPhase);
            }

            // --- aditiva: fundamental + 2 parciales ---
            float x = Osc(ph + mod);
            if (_p.partial2 > 0f) x += _p.partial2 * Osc(2f * ph + mod);
            if (_p.partial3 > 0f) x += _p.partial3 * Osc(3f * ph);
            x /= 1f + _p.partial2 + _p.partial3;

            if (_p.noise > 0f) x += _p.noise * Noise();

            // --- filtro low-pass de un polo ---
            if (_p.cutoff > 0f) { _lp += _lpCoef * (x - _lp); x = _lp; }

            // --- tremolo (LFO) ---
            if (_p.lfoDepth > 0f)
            {
                _lfoPhase += TwoPi * _p.lfoRate / _sr;
                if (_lfoPhase > TwoPi) _lfoPhase -= TwoPi;
                e *= 1f - _p.lfoDepth * (0.5f + 0.5f * Mathf.Sin((float)_lfoPhase));
            }

            return x * e * _p.amp;
        }
    }

    /// <summary>Pool de voces (polifonía). Si se llena, roba la más antigua.</summary>
    public class VoiceBank
    {
        public readonly object Lock = new object();
        private readonly SynthVoice[] _voices;

        public VoiceBank(int count, int sampleRate)
        {
            _voices = new SynthVoice[count];
            for (int i = 0; i < count; i++)
                _voices[i] = new SynthVoice(sampleRate, 2463534242u + (uint)i * 7919u);
        }

        public void Trigger(SynthPatch p)
        {
            lock (Lock)
            {
                SynthVoice pick = null;
                foreach (var v in _voices) if (!v.Active) { pick = v; break; }
                if (pick == null)
                {
                    long max = -1;
                    foreach (var v in _voices) if (v.Age > max) { max = v.Age; pick = v; }
                }
                pick.Start(p);
            }
        }

        /// <summary>Suma de todas las voces. Llamar dentro de lock(Lock).</summary>
        public float Mix()
        {
            float s = 0f;
            for (int i = 0; i < _voices.Length; i++)
                if (_voices[i].Active) s += _voices[i].Next();
            return s;
        }
    }

    public static class ProcUtil
    {
        public static float Midi(float n) { return 440f * Mathf.Pow(2f, (n - 69f) / 12f); }

        /// <summary>OnAudioFilterRead solo corre si el AudioSource está sonando: le damos un clip silencioso en loop.</summary>
        public static void PrepareSource(AudioSource src)
        {
            src.clip = AudioClip.Create("proc_silence", 4410, 1, 44100, false);
            src.loop = true;
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            src.volume = 1f;
            src.Play();
        }
    }
}