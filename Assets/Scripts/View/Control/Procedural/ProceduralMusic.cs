//using System;
//using UnityEngine;

//namespace View.Control.Procedural
//{
//    /// <summary>
//    /// Música generativa por capas (La menor pentatónica):
//    ///  1) Dron grave: wavetable A2 + seno E3, LFO lento de amplitud, low-pass que se abre con el progreso.
//    ///  2) Motivo: notas "campana" (aditiva) con caminata aleatoria por la escala; la densidad sube con el progreso.
//    ///  3) Textura aguda: parciales A5/E6 con tremolo muy lento; su nivel sube con el progreso.
//    /// </summary>
//    [RequireComponent(typeof(AudioSource))]
//    public class ProceduralMusic : MonoBehaviour
//    {
//        private const double TwoPi = Math.PI * 2.0;
//        // A3 C4 D4 E4 G4 A4 C5 D5 E5 G5
//        private static readonly int[] Scale = { 57, 60, 62, 64, 67, 69, 72, 74, 76, 79 };

//        public float Bpm = 54f;
//        [Range(0f, 1f)] public float Progress; // 0 = inicio de nivel, 1 = cerca de la solución

//        private VoiceBank _bank;
//        private int _sr;
//        private readonly System.Random _rng = new System.Random(1234);

//        private float _gain, _targetGain, _gainCoef = 0.001f;
//        private float _prog;
//        private double _d1, _d2, _lfoDrone, _t1, _t2, _lfoTex;
//        private double _incD1, _incD2, _incLfoDrone, _incT1, _incT2, _incLfoTex;
//        private float _droneLp;
//        private int _stepSamples, _toNext, _idx = 5;

//        // Ducking (la música cede espacio a los efectos)
//        private float _duck, _duckEnv, _duckRelCoef = 0.9999f;
//        private volatile float _duckPending;
//        private long _duckDelay;
//        private float _duckAtkCoef;

//        private void Awake()
//        {
//            _sr = AudioSettings.outputSampleRate;
//            _bank = new VoiceBank(12, _sr); // 12 voces: motivo + eco + cadencia final (7)
//            _incD1 = TwoPi * 110.0 / _sr;
//            _incD2 = TwoPi * 164.81 * 1.001 / _sr; // pequeño detune
//            _incLfoDrone = TwoPi * 0.06 / _sr;
//            _incT1 = TwoPi * 880.0 / _sr;
//            _incT2 = TwoPi * 1318.5 / _sr;
//            _incLfoTex = TwoPi * 0.13 / _sr;
//            _stepSamples = (int)(_sr * 60f / Bpm * 0.5f); // corcheas
//            _toNext = _stepSamples;
//            _duckAtkCoef = 1f - Mathf.Exp(-1f / (0.01f * _sr)); // ataque del ducking ~10 ms
//            ProcUtil.PrepareSource(GetComponent<AudioSource>());
//        }

//        /// <summary>Fade de volumen de la música (0 = silencio). No afecta a los SFX.</summary>
//        public void FadeTo(float target, float seconds)
//        {
//            _targetGain = Mathf.Max(0f, target);
//            _gainCoef = seconds <= 0.01f ? 1f : 1f - Mathf.Exp(-3f / (seconds * _sr));
//        }

//        public void SetProgress(float p) { Progress = Mathf.Clamp01(p); }

//        /// <summary>Baja la música 'amount' (0-1) y la recupera en 'release' segundos.</summary>
//        public void Duck(float amount, float release, float delay = 0f)
//        {
//            if (_sr == 0) return;
//            _duckRelCoef = Mathf.Exp(-3f / (Mathf.Max(release, 0.05f) * _sr));
//            _duckDelay = (long)(Mathf.Max(0f, delay) * _sr);
//            _duckPending = Mathf.Max(_duckPending, Mathf.Clamp01(amount));
//        }

//        /// <summary>Cierre de nivel: cadencia breve (D-G suspendido -> La menor) tras el arpegio de victoria.</summary>
//        public void Resolve()
//        {
//            if (_bank == null) return;
//            int[][] chords = { new[] { 62, 67, 74 }, new[] { 57, 60, 64, 69 } };
//            float[] times = { 0.55f, 1.05f };
//            for (int c = 0; c < chords.Length; c++)
//                foreach (int n in chords[c])
//                    _bank.Trigger(new SynthPatch
//                    {
//                        wave = SynthWave.Wavetable,
//                        freqStart = ProcUtil.Midi(n),
//                        attack = 0.06f,
//                        decay = 0.5f,
//                        sustain = 0.5f,
//                        hold = c == 0 ? 0.45f : 0.9f,
//                        release = c == 0 ? 0.4f : 2.2f,
//                        amp = 0.06f,
//                        cutoff = 1800f,
//                        lfoRate = 4f,
//                        lfoDepth = 0.12f,
//                        delay = times[c]
//                    });
//        }

//        private static double Wrap(double p) { return p > TwoPi ? p - TwoPi : p; }

//        private void Step() // se llama en el hilo de audio, dentro del lock
//        {
//            float prob = Mathf.Lerp(0.12f, 0.5f, _prog);
//            if (_rng.NextDouble() > prob) return;

//            _idx = Mathf.Clamp(_idx + _rng.Next(-2, 3), 0, Scale.Length - 1);
//            float f = ProcUtil.Midi(Scale[_idx]);
//            _bank.Trigger(new SynthPatch
//            {
//                wave = SynthWave.Sine,
//                freqStart = f,
//                attack = 0.01f,
//                decay = 1.2f,
//                sustain = 0f,
//                hold = 0.3f,
//                release = 1.2f,
//                amp = 0.12f,
//                partial2 = 0.35f,
//                partial3 = 0.12f,
//                cutoff = 3000f
//            });

//            if (_prog > 0.6f && _rng.NextDouble() < 0.35) // eco una octava arriba cuando hay más energía
//            {
//                _bank.Trigger(new SynthPatch
//                {
//                    wave = SynthWave.Sine,
//                    freqStart = f * 2f,
//                    attack = 0.01f,
//                    decay = 1f,
//                    sustain = 0f,
//                    hold = 0.2f,
//                    release = 1f,
//                    amp = 0.05f,
//                    partial2 = 0.2f,
//                    delay = 0.25f
//                });
//            }
//        }

//        private void OnAudioFilterRead(float[] data, int channels)
//        {
//            if (_bank == null) return;
//            int frames = data.Length / channels;

//            float cutoff = 250f + _prog * 1200f; // el progreso abre el filtro del dron
//            float a = 1f - Mathf.Exp(-(float)TwoPi * cutoff / _sr);
//            float target = Progress;

//            lock (_bank.Lock)
//            {
//                for (int i = 0; i < frames; i++)
//                {
//                    _gain += (_targetGain - _gain) * _gainCoef;

//                    // --- ducking: retardo opcional, ataque rápido, release exponencial ---
//                    if (_duckPending > 0f)
//                    {
//                        if (_duckDelay > 0) _duckDelay--;
//                        else { _duck = Mathf.Max(_duck, _duckPending); _duckPending = 0f; }
//                    }
//                    _duck *= _duckRelCoef;
//                    _duckEnv += (_duck - _duckEnv) * _duckAtkCoef;
//                    _prog += (target - _prog) * 0.00002f; // suavizado ~1 s

//                    if (--_toNext <= 0) { _toNext += _stepSamples; Step(); }

//                    _d1 = Wrap(_d1 + _incD1);
//                    _d2 = Wrap(_d2 + _incD2);
//                    _lfoDrone = Wrap(_lfoDrone + _incLfoDrone);
//                    _t1 = Wrap(_t1 + _incT1);
//                    _t2 = Wrap(_t2 + _incT2);
//                    _lfoTex = Wrap(_lfoTex + _incLfoTex);

//                    float lfo = 0.6f + 0.4f * Mathf.Sin((float)_lfoDrone);
//                    // wavetable (fundamental + 3 armónicos decrecientes) sobre A2, seno puro en E3
//                    float drone = (WaveTable.Read((float)_d1) + 0.6f * Mathf.Sin((float)_d2)) * 0.10f * lfo;
//                    _droneLp += a * (drone - _droneLp);

//                    float texLfo = 0.5f + 0.5f * Mathf.Sin((float)_lfoTex);
//                    float tex = (Mathf.Sin((float)_t1) + 0.6f * Mathf.Sin((float)_t2))
//                                * 0.02f * (0.2f + 0.8f * _prog) * texLfo;

//                    float s = (_droneLp + tex + _bank.Mix()) * _gain * (1f - _duckEnv);
//                    s = (float)Math.Tanh(s) * 0.9f;
//                    for (int c = 0; c < channels; c++) data[i * channels + c] = s;
//                }
//            }
//        }
//    }
//}

using System;
using UnityEngine;

namespace View.Control.Procedural
{
    /// <summary>
    /// Música generativa DINÁMICA. Todo se sintetiza; nada es un loop grabado.
    ///
    /// Armonía: progresión de acordes que cambia en cada compás.
    ///   Sección A: Am - F - C - G      Sección B: Am - Dm - F - E   (forma A A A B, se repite)
    ///
    /// Capas (entran según el progreso del nivel, 0 a 1):
    ///   siempre   -> pad de acordes (wavetable), sub-dron que sigue la fundamental, bajo "bwow"
    ///   >= 0.20   -> hi-hats en contratiempo
    ///   >= 0.30   -> el bajo se vuelve sincopado
    ///   >= 0.40   -> bombo
    ///   >= 0.50   -> hi-hats en semicorcheas
    ///   >= 0.55   -> palmas (snare de ruido)
    ///   >= 0.60   -> theremin alien (frases con glide y vibrato) + bajo con saltos de octava
    ///   >= 0.65   -> arpegio en semicorcheas
    ///   >= 0.70   -> bombo sincopado
    /// Además: el tempo sube de BpmMin a BpmMax, el filtro del pad se abre, el arpegio se vuelve más denso,
    /// cada vez que entra una capa nueva suena un platillo, y cada 8 compases hay un redoble.
    /// Arpegio y theremin pasan por un eco (delay de corchea con puntillo).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ProceduralMusic : MonoBehaviour
    {
        private const double TwoPi = Math.PI * 2.0;

        [Header("Tempo")]
        public float BpmMin = 92f;
        public float BpmMax = 110f;
        [Tooltip("Swing de las semicorcheas impares (0 = recto, 0.3 = muy swing).")]
        [Range(0f, 0.4f)] public float Swing = 0.12f;

        [Header("Eco del arpegio y el theremin")]
        [Range(0f, 0.8f)] public float EchoFeedback = 0.35f;
        [Range(0f, 1f)] public float EchoMix = 0.35f;

        [Range(0f, 1f)] public float Progress; // 0 = inicio de nivel, 1 = cerca de la solución

        // --- Armonía ---
        private static readonly int[][] ProgA = { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 60, 64, 67 }, new[] { 55, 59, 62 } }; // Am F C G
        private static readonly int[][] ProgB = { new[] { 57, 60, 64 }, new[] { 50, 53, 57 }, new[] { 53, 57, 60 }, new[] { 52, 56, 59 } }; // Am Dm F E
        private static readonly int[] LeadScale = { 69, 72, 74, 76, 79, 81 }; // La menor pentatónica (registro agudo)

        private VoiceBank _bank;    // pad, bajo, batería (seco)
        private VoiceBank _fxBank;  // arpegio y theremin (pasan por el eco)
        private int _sr;
        private readonly System.Random _rng = new System.Random(1234);

        // Volumen y progreso
        private float _gain, _targetGain, _gainCoef = 0.001f;
        private float _prog;

        // Secuenciador
        private int _step;            // 0..15 dentro del compás
        private int _bar;
        private int _toNext = 1;
        private int _stepSamples;
        private int[] _chord = ProgA[0];
        private int _arpIdx;
        private int _lastLayer;
        private float _leadPrevFreq;
        private int _leadNotesLeft;

        // Sub-dron que sigue la fundamental del acorde
        private double _subPhase, _subLfo;
        private float _subFreq = 55f, _subTarget = 55f;

        // Eco
        private float[] _echo;
        private int _echoW, _echoLen;
        private float _echoLp;

        // Ducking (la música cede espacio a los efectos)
        private float _duck, _duckEnv, _duckRelCoef = 0.9999f;
        private volatile float _duckPending;
        private long _duckDelay;
        private float _duckAtkCoef;

        private void Awake()
        {
            _sr = AudioSettings.outputSampleRate;
            _bank = new VoiceBank(24, _sr);
            _fxBank = new VoiceBank(10, _sr);
            _echo = new float[_sr * 2];
            _echoLen = _sr / 2;
            _stepSamples = StepSamples(BpmMin);
            _duckAtkCoef = 1f - Mathf.Exp(-1f / (0.01f * _sr)); // ataque del ducking ~10 ms
            ProcUtil.PrepareSource(GetComponent<AudioSource>());
        }

        private int StepSamples(float bpm) { return Mathf.Max(1, (int)(_sr * 60f / bpm / 4f)); } // semicorchea

        /// <summary>Fade de volumen de la música (0 = silencio). No afecta a los SFX.</summary>
        public void FadeTo(float target, float seconds)
        {
            _targetGain = Mathf.Max(0f, target);
            _gainCoef = seconds <= 0.01f ? 1f : 1f - Mathf.Exp(-3f / (seconds * _sr));
        }

        public void SetProgress(float p) { Progress = Mathf.Clamp01(p); }

        /// <summary>Baja la música 'amount' (0-1) y la recupera en 'release' segundos.</summary>
        public void Duck(float amount, float release, float delay = 0f)
        {
            if (_sr == 0) return;
            _duckRelCoef = Mathf.Exp(-3f / (Mathf.Max(release, 0.05f) * _sr));
            _duckDelay = (long)(Mathf.Max(0f, delay) * _sr);
            _duckPending = Mathf.Max(_duckPending, Mathf.Clamp01(amount));
        }

        /// <summary>Cierre de nivel: cadencia V -> I (Mi mayor -> La mayor) con vibrato, tras la fanfarria.</summary>
        public void Resolve()
        {
            if (_bank == null) return;
            int[][] chords = { new[] { 52, 56, 59, 64 }, new[] { 57, 61, 64, 69, 76 } };
            float[] times = { 0.5f, 0.95f };
            for (int c = 0; c < chords.Length; c++)
                foreach (int n in chords[c])
                    _bank.Trigger(new SynthPatch
                    {
                        wave = SynthWave.Wavetable,
                        freqStart = ProcUtil.Midi(n),
                        attack = 0.03f,
                        decay = 0.5f,
                        sustain = 0.5f,
                        hold = c == 0 ? 0.4f : 1f,
                        release = c == 0 ? 0.3f : 2.2f,
                        amp = 0.055f,
                        cutoff = 2500f,
                        vibRate = 5f,
                        vibDepth = c == 0 ? 0f : 0.3f,
                        delay = times[c]
                    });
            // golpe de bombo + platillo en el acorde final
            Kick(0.95f);
            Crash(0.95f);
        }

        // ------------------------------------------------------------------------------
        // Instrumentos (todos son SynthPatch: misma voz del sintetizador, otros parámetros)
        // ------------------------------------------------------------------------------

        private void Kick(float delay = 0f)
        {
            // seno con caída de altura muy rápida 160 -> 45 Hz: el "pum" clásico de una caja de ritmos
            _bank.Trigger(new SynthPatch
            {
                wave = SynthWave.Sine,
                freqStart = 160f,
                freqEnd = 45f,
                glideTime = 0.07f,
                attack = 0.001f,
                decay = 0.2f,
                sustain = 0f,
                hold = 0.01f,
                release = 0.15f,
                amp = 0.3f,
                delay = delay
            });
        }

        private void Hat(float amp, float delay)
        {
            // ruido blanco con high-pass a 7 kHz, 30 ms
            _bank.Trigger(new SynthPatch
            {
                wave = SynthWave.Sine,
                freqStart = 1f,
                noise = 1f,
                hpCutoff = 7000f,
                attack = 0.001f,
                decay = 0.03f,
                sustain = 0f,
                hold = 0.005f,
                release = 0.02f,
                amp = amp,
                delay = delay
            });
        }

        private void Clap(float delay)
        {
            // ruido con banda media (HP 1.2 kHz + LP 5 kHz) + cuerpo tonal corto
            _bank.Trigger(new SynthPatch
            {
                wave = SynthWave.Sine,
                freqStart = 1f,
                noise = 1f,
                hpCutoff = 1200f,
                cutoff = 5000f,
                attack = 0.001f,
                decay = 0.12f,
                sustain = 0f,
                hold = 0.01f,
                release = 0.08f,
                amp = 0.16f,
                delay = delay
            });
            _bank.Trigger(new SynthPatch
            {
                wave = SynthWave.Triangle,
                freqStart = 220f,
                freqEnd = 180f,
                glideTime = 0.05f,
                attack = 0.001f,
                decay = 0.06f,
                sustain = 0f,
                hold = 0.01f,
                release = 0.04f,
                amp = 0.06f,
                delay = delay
            });
        }

        private void Crash(float delay = 0f)
        {
            // platillo: ruido agudo con cola larga; anuncia que entró una capa nueva
            _bank.Trigger(new SynthPatch
            {
                wave = SynthWave.Sine,
                freqStart = 1f,
                noise = 1f,
                hpCutoff = 4000f,
                cutoff = 12000f,
                attack = 0.003f,
                decay = 0.9f,
                sustain = 0f,
                hold = 0.01f,
                release = 0.6f,
                amp = 0.08f,
                delay = delay
            });
        }

        private void Bass(int midi, bool octaveJump, float delay)
        {
            // "bwow": triangular que cae desde una octava arriba + FM que se cierra
            float f = ProcUtil.Midi(midi) * (octaveJump ? 2f : 1f);
            _bank.Trigger(new SynthPatch
            {
                wave = SynthWave.Triangle,
                freqStart = f * 2f,
                freqEnd = f,
                glideTime = 0.05f,
                attack = 0.003f,
                decay = 0.15f,
                sustain = 0.4f,
                hold = 0.12f,
                release = 0.08f,
                fmRatio = 1f,
                fmIndex = 1.5f,
                fmIndexEnd = 0.2f,
                fmTime = 0.12f,
                cutoff = 900f,
                amp = 0.2f,
                delay = delay
            });
        }

        private void Pad(int[] chord, float barSec)
        {
            float cutoff = 500f + _prog * 2500f; // el progreso abre el filtro
            for (int i = 0; i < chord.Length; i++)
                _bank.Trigger(new SynthPatch
                {
                    wave = SynthWave.Wavetable,
                    freqStart = ProcUtil.Midi(chord[i]) * (1f + 0.002f * (i - 1)), // leve detune
                    attack = 0.25f,
                    decay = 0.4f,
                    sustain = 0.7f,
                    hold = barSec * 0.9f,
                    release = 0.8f,
                    cutoff = cutoff,
                    lfoRate = 0.5f,
                    lfoDepth = 0.25f,
                    amp = 0.045f
                });
        }

        private void Arp(float delay, bool high)
        {
            int n = _chord[_arpIdx % _chord.Length] + 12 + (high ? 12 : 0);
            _arpIdx++;
            float f = ProcUtil.Midi(n);
            _fxBank.Trigger(new SynthPatch
            {
                wave = SynthWave.Sine,
                freqStart = f,
                attack = 0.002f,
                decay = 0.18f,
                sustain = 0f,
                hold = 0.02f,
                release = 0.15f,
                partial2 = 0.35f,
                partial3 = 0.15f,
                cutoff = 4000f,
                amp = 0.07f,
                delay = delay
            });
        }

        private void LeadNote(float stepSec)
        {
            // theremin alien: wavetable con glide desde la nota anterior y vibrato ancho
            int midi = LeadScale[_rng.Next(LeadScale.Length)];
            float f = ProcUtil.Midi(midi);
            float from = _leadPrevFreq > 0f ? _leadPrevFreq : f * 0.75f;
            _leadPrevFreq = f;
            _fxBank.Trigger(new SynthPatch
            {
                wave = SynthWave.Wavetable,
                freqStart = from,
                freqEnd = f,
                glideTime = 0.08f,
                attack = 0.03f,
                decay = 0.1f,
                sustain = 0.8f,
                hold = stepSec * 2.5f,
                release = 0.25f,
                vibRate = 6f,
                vibDepth = 0.6f,
                cutoff = 3000f,
                amp = 0.07f
            });
        }

        // ------------------------------------------------------------------------------
        // Secuenciador: se llama en el hilo de audio una vez por semicorchea
        // ------------------------------------------------------------------------------
        private void Step()
        {
            float bpm = Mathf.Lerp(BpmMin, BpmMax, _prog);
            _stepSamples = StepSamples(bpm);
            float stepSec = _stepSamples / (float)_sr;
            float swing = (_step % 2 == 1) ? Swing * stepSec : 0f; // las semicorcheas impares llegan tarde
            float L = _prog;

            // --- inicio de compás: acorde nuevo, pad, eco, platillo si entró una capa ---
            if (_step == 0)
            {
                int[][] prog = (_bar / 4) % 4 == 3 ? ProgB : ProgA;  // A A A B
                _chord = prog[_bar % 4];
                _subTarget = ProcUtil.Midi(_chord[0] - 24);
                _arpIdx = 0;
                Pad(_chord, stepSec * 16f);
                _echoLen = Mathf.Clamp((int)(stepSec * 3f * _sr), 1, _echo.Length - 1); // corchea con puntillo

                int layer = Mathf.FloorToInt(L * 5f);            // 5 escalones: 0.2, 0.4, 0.6, 0.8, 1.0
                if (layer > _lastLayer && layer >= 1) Crash();
                _lastLayer = layer;

                if (L >= 0.6f && _bar % 2 == 0 && _rng.NextDouble() < 0.6) _leadNotesLeft = 2 + _rng.Next(3);
            }

            // --- bajo ---
            int root = _chord[0] - 12;
            bool jump = L >= 0.6f && (_step == 3 || _step == 11);
            bool bassHit = _step == 0 || _step == 8
                           || (L >= 0.3f && (_step == 6 || _step == 14))
                           || jump;
            if (bassHit) Bass(root, jump, swing);

            // --- batería ---
            if (L >= 0.4f && (_step == 0 || _step == 8)) Kick(swing);
            if (L >= 0.7f && _step == 10) Kick(swing);
            if (L >= 0.55f && (_step == 4 || _step == 12)) Clap(swing);

            if (L >= 0.5f)
            {
                bool odd = _step % 2 == 1;
                if (!odd || _rng.NextDouble() < 0.7)
                    Hat(_step % 4 == 2 ? 0.06f : 0.035f, swing);  // acento en el contratiempo
            }
            else if (L >= 0.2f && _step % 4 == 2)
            {
                Hat(0.05f, swing);
            }

            // redoble cada 8 compases, en el último tiempo
            if (L >= 0.5f && _bar % 8 == 7 && _step >= 13) Clap(swing);

            // --- arpegio ---
            bool arpGrid = L >= 0.65f || _step % 2 == 0;
            float arpProb = Mathf.Lerp(0.25f, 0.9f, L);
            if (arpGrid && _rng.NextDouble() < arpProb)
                Arp(swing, L > 0.8f && _rng.NextDouble() < 0.3);

            // --- theremin alien: una nota cada 4 semicorcheas mientras dure la frase ---
            if (_leadNotesLeft > 0 && _step % 4 == 0)
            {
                LeadNote(stepSec);
                _leadNotesLeft--;
            }

            // avanzar
            _step++;
            if (_step >= 16) { _step = 0; _bar++; }
        }

        private static double Wrap(double p) { return p > TwoPi ? p - TwoPi : p; }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_bank == null) return;
            int frames = data.Length / channels;
            float target = Progress;
            double subLfoInc = TwoPi * 0.25 / _sr;

            lock (_bank.Lock)
            {
                lock (_fxBank.Lock)
                {
                    for (int i = 0; i < frames; i++)
                    {
                        _gain += (_targetGain - _gain) * _gainCoef;
                        _prog += (target - _prog) * 0.00002f; // suavizado ~1 s

                        // --- ducking: retardo opcional, ataque rápido, release exponencial ---
                        if (_duckPending > 0f)
                        {
                            if (_duckDelay > 0) _duckDelay--;
                            else { _duck = Mathf.Max(_duck, _duckPending); _duckPending = 0f; }
                        }
                        _duck *= _duckRelCoef;
                        _duckEnv += (_duck - _duckEnv) * _duckAtkCoef;

                        if (--_toNext <= 0) { Step(); _toNext = _stepSamples; }

                        // --- sub-dron: sigue la fundamental con glide (portamento) y respira con un LFO ---
                        _subFreq += (_subTarget - _subFreq) * 0.0005f;
                        _subPhase = Wrap(_subPhase + TwoPi * _subFreq / _sr);
                        _subLfo = Wrap(_subLfo + subLfoInc);
                        float sub = Mathf.Sin((float)_subPhase) * 0.06f * (0.7f + 0.3f * Mathf.Sin((float)_subLfo));

                        // --- eco: lo del fxBank se repite cada corchea con puntillo, cada vez más oscuro ---
                        float fx = _fxBank.Mix();
                        int r = _echoW - _echoLen; if (r < 0) r += _echo.Length;
                        float echoed = _echo[r];
                        _echoLp += 0.35f * (echoed - _echoLp);
                        _echo[_echoW] = fx + _echoLp * EchoFeedback;
                        if (++_echoW >= _echo.Length) _echoW = 0;

                        float s = (sub + _bank.Mix() + fx + _echoLp * EchoMix) * _gain * (1f - _duckEnv);
                        s = (float)Math.Tanh(s) * 0.9f;
                        for (int c = 0; c < channels; c++) data[i * channels + c] = s;
                    }
                }
            }
        }
    }
}
