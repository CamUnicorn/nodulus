using System;
using UnityEngine;

namespace View.Control.Procedural
{
    /// <summary>
    /// Música generativa por capas (La menor pentatónica):
    ///  1) Dron grave: senos A2 + E3 + 2º armónico, LFO lento de amplitud, low-pass que se abre con el progreso.
    ///  2) Motivo: notas "campana" (aditiva) con caminata aleatoria por la escala; la densidad sube con el progreso.
    ///  3) Textura aguda: parciales A5/E6 con tremolo muy lento; su nivel sube con el progreso.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ProceduralMusic : MonoBehaviour
    {
        private const double TwoPi = Math.PI * 2.0;
        // A3 C4 D4 E4 G4 A4 C5 D5 E5 G5
        private static readonly int[] Scale = { 57, 60, 62, 64, 67, 69, 72, 74, 76, 79 };

        public float Bpm = 54f;
        [Range(0f, 1f)] public float Progress; // 0 = inicio de nivel, 1 = cerca de la solución

        private VoiceBank _bank;
        private int _sr;
        private readonly System.Random _rng = new System.Random(1234);

        private float _gain, _targetGain, _gainCoef = 0.001f;
        private float _prog;
        private double _d1, _d2, _lfoDrone, _t1, _t2, _lfoTex;
        private double _incD1, _incD2, _incLfoDrone, _incT1, _incT2, _incLfoTex;
        private float _droneLp;
        private int _stepSamples, _toNext, _idx = 5;

        private void Awake()
        {
            _sr = AudioSettings.outputSampleRate;
            _bank = new VoiceBank(8, _sr);
            _incD1 = TwoPi * 110.0 / _sr;
            _incD2 = TwoPi * 164.81 * 1.001 / _sr; // pequeño detune
            _incLfoDrone = TwoPi * 0.06 / _sr;
            _incT1 = TwoPi * 880.0 / _sr;
            _incT2 = TwoPi * 1318.5 / _sr;
            _incLfoTex = TwoPi * 0.13 / _sr;
            _stepSamples = (int)(_sr * 60f / Bpm * 0.5f); // corcheas
            _toNext = _stepSamples;
            ProcUtil.PrepareSource(GetComponent<AudioSource>());
        }

        /// <summary>Fade de volumen de la música (0 = silencio). No afecta a los SFX.</summary>
        public void FadeTo(float target, float seconds)
        {
            _targetGain = Mathf.Max(0f, target);
            _gainCoef = seconds <= 0.01f ? 1f : 1f - Mathf.Exp(-3f / (seconds * _sr));
        }

        public void SetProgress(float p) { Progress = Mathf.Clamp01(p); }

        private static double Wrap(double p) { return p > TwoPi ? p - TwoPi : p; }

        private void Step() // se llama en el hilo de audio, dentro del lock
        {
            float prob = Mathf.Lerp(0.12f, 0.5f, _prog);
            if (_rng.NextDouble() > prob) return;

            _idx = Mathf.Clamp(_idx + _rng.Next(-2, 3), 0, Scale.Length - 1);
            float f = ProcUtil.Midi(Scale[_idx]);
            _bank.Trigger(new SynthPatch
            {
                wave = SynthWave.Sine,
                freqStart = f,
                attack = 0.01f,
                decay = 1.2f,
                sustain = 0f,
                hold = 0.3f,
                release = 1.2f,
                amp = 0.12f,
                partial2 = 0.35f,
                partial3 = 0.12f,
                cutoff = 3000f
            });

            if (_prog > 0.6f && _rng.NextDouble() < 0.35) // eco una octava arriba cuando hay más energía
            {
                _bank.Trigger(new SynthPatch
                {
                    wave = SynthWave.Sine,
                    freqStart = f * 2f,
                    attack = 0.01f,
                    decay = 1f,
                    sustain = 0f,
                    hold = 0.2f,
                    release = 1f,
                    amp = 0.05f,
                    partial2 = 0.2f,
                    delay = 0.25f
                });
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_bank == null) return;
            int frames = data.Length / channels;

            float cutoff = 250f + _prog * 1200f; // el progreso abre el filtro del dron
            float a = 1f - Mathf.Exp(-(float)TwoPi * cutoff / _sr);
            float target = Progress;

            lock (_bank.Lock)
            {
                for (int i = 0; i < frames; i++)
                {
                    _gain += (_targetGain - _gain) * _gainCoef;
                    _prog += (target - _prog) * 0.00002f; // suavizado ~1 s

                    if (--_toNext <= 0) { _toNext += _stepSamples; Step(); }

                    _d1 = Wrap(_d1 + _incD1);
                    _d2 = Wrap(_d2 + _incD2);
                    _lfoDrone = Wrap(_lfoDrone + _incLfoDrone);
                    _t1 = Wrap(_t1 + _incT1);
                    _t2 = Wrap(_t2 + _incT2);
                    _lfoTex = Wrap(_lfoTex + _incLfoTex);

                    float lfo = 0.6f + 0.4f * Mathf.Sin((float)_lfoDrone);
                    float drone = (Mathf.Sin((float)_d1) + 0.6f * Mathf.Sin((float)_d2)
                                   + 0.25f * Mathf.Sin(2f * (float)_d1)) * 0.10f * lfo;
                    _droneLp += a * (drone - _droneLp);

                    float texLfo = 0.5f + 0.5f * Mathf.Sin((float)_lfoTex);
                    float tex = (Mathf.Sin((float)_t1) + 0.6f * Mathf.Sin((float)_t2))
                                * 0.02f * (0.2f + 0.8f * _prog) * texLfo;

                    float s = (_droneLp + tex + _bank.Mix()) * _gain;
                    s = (float)Math.Tanh(s) * 0.9f;
                    for (int c = 0; c < channels; c++) data[i * channels + c] = s;
                }
            }
        }
    }
}