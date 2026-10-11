using UnityEngine;

namespace View.Control.Procedural
{
    /// <summary>Eventos sonoros del juego (el "SoundEventMap" de la documentación).</summary>
    public enum NodulusSoundEvent
    {
        // Movimiento / piezas
        GridAppear, NodeEnter, NodeLeave,
        PushHigh, PullHigh, PushMid, PullMid, PushLow, PullLow,
        ArcHigh, ArcMid, ArcLow,
        Rotate90, InvalidMove,
        // Progreso
        LevelSolved, LevelEnable, GameEnd,
        // Interfaz (deben ir al final: IsUi() compara con UiConfirm)
        UiConfirm, UiBack, UiHover, UiPause, UiRestart
    }

    /// <summary>
    /// Efectos de sonido procedurales.
    /// MOVIMIENTO: paleta "alien / caricatura": glides de una octava, vibrato (LFO de altura),
    ///             FM con índice que cambia durante la nota ("bwOOp", "wah"), resortes ("boing").
    /// INTERFAZ:   discreta y corta, para no competir con el tablero.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ProceduralSfx : MonoBehaviour
    {
        [Range(0f, 1f)] public float Volume = 1f;     // bus maestro de efectos (antes del limitador)
        [Range(0f, 1f)] public float MoveGain = 0.8f; // familia de movimiento / tablero
        [Range(0f, 1f)] public float UiVolume = 0.6f; // familia de interfaz

        [Header("Carácter alien del movimiento")]
        [Tooltip("Variación aleatoria de altura por disparo (0.03 = ±3%). Más alto = más 'voces' distintas.")]
        [Range(0f, 0.1f)] public float AlienPitchJitter = 0.03f;
        [Tooltip("Multiplica la profundidad de todos los vibratos del movimiento. 0 = sin temblor, 2 = muy exagerado.")]
        [Range(0f, 2f)] public float WobbleAmount = 1f;

        private VoiceBank _bank;
        // Contexto del disparo actual (solo hilo principal)
        private float _gain = 1f, _jit = 1f, _vib = 1f, _delay;

        private void Awake()
        {
            _bank = new VoiceBank(20, AudioSettings.outputSampleRate);
            ProcUtil.PrepareSource(GetComponent<AudioSource>());
        }

        private static float M(float midi) { return ProcUtil.Midi(midi); }

        /// <summary>
        /// Dispara una nota. Orden: onda, frecuencia inicial y final, tiempo de glide,
        /// ADSR (A, D, S, hold, R), amplitud; luego parámetros opcionales con nombre.
        /// </summary>
        private void Tone(SynthWave w, float f0, float f1, float glide,
                          float a, float d, float s, float hold, float r, float amp,
                          float at = 0f, float p2 = 0f, float p3 = 0f,
                          float fmR = 0f, float fmI = 0f, float noise = 0f,
                          float cutoff = 0f, float lfoR = 0f, float lfoD = 0f,
                          float vibR = 0f, float vibD = 0f, float vibDec = 0f,
                          float fmEnd = 0f, float fmT = 0f, float hp = 0f)
        {
            _bank.Trigger(new SynthPatch
            {
                wave = w,
                freqStart = f0 * _jit,
                freqEnd = f1 * _jit,
                glideTime = glide,
                attack = a,
                decay = d,
                sustain = s,
                hold = hold,
                release = r,
                amp = amp * _gain,
                partial2 = p2,
                partial3 = p3,
                fmRatio = fmR,
                fmIndex = fmI,
                noise = noise,
                cutoff = cutoff,
                lfoRate = lfoR,
                lfoDepth = lfoD,
                vibRate = vibR * UnityEngine.Random.Range(0.85f, 1.15f), // cada "criatura" tiembla distinto
                vibDepth = vibD * _vib,
                vibDecay = vibDec,
                fmIndexEnd = fmEnd,
                fmTime = fmT,
                hpCutoff = hp,
                delay = _delay + at
            });
        }

        // ---------------------------------------------------------------------------------
        // Gestos de movimiento reutilizables
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Empujar = "bwOOP!": sube una octava, la FM empieza áspera y se limpia (índice 3 -> 0),
        /// con un temblor rápido que se apaga y un "pop" agudo al final.
        /// Tirar = "bwEEow": baja una octava, la FM se ensucia al final y tiembla lento (triste/cómico).
        /// </summary>
        private void Slide(float midi, bool push)
        {
            float f = M(midi);
            if (push)
            {
                Tone(SynthWave.Sine, f, f * 2f, 0.14f, 0.005f, 0.12f, 0.2f, 0.1f, 0.1f, 0.3f,
                     fmR: 1.5f, fmI: 3f, fmEnd: 0f, fmT: 0.12f,
                     vibR: 18f, vibD: 0.6f, vibDec: 0.15f);
                Tone(SynthWave.Sine, f * 2.5f, f * 2.5f, 0f, 0.002f, 0.04f, 0f, 0.01f, 0.04f, 0.12f, at: 0.13f);
            }
            else
            {
                Tone(SynthWave.Sine, f * 2f, f, 0.16f, 0.005f, 0.12f, 0.25f, 0.12f, 0.12f, 0.3f,
                     fmR: 1.5f, fmI: 0f, fmEnd: 3f, fmT: 0.2f,
                     vibR: 7f, vibD: 0.8f);
            }
        }

        /// <summary>Viaje de la varilla en arco: "wooo-ooo" de theremin, glide de quinta y vibrato ancho.</summary>
        private void Arc(float midi)
        {
            float f = M(midi);
            Tone(SynthWave.Sine, f, f * 1.5f, 0.25f, 0.02f, 0.1f, 0.6f, 0.2f, 0.15f, 0.2f,
                 p2: 0.2f, vibR: 6f, vibD: 1.2f, cutoff: 3500f);
        }

        public void Trigger(NodulusSoundEvent e, float intensity = 1f, float delay = 0f)
        {
            if (_bank == null) return;
            bool ui = IsUi(e);
            _gain = Mathf.Clamp01(intensity) * (ui ? UiVolume : MoveGain);
            // Movimiento: variación grande (cada pieza "habla" un poco distinto). UI: casi fija.
            float j = ui ? 0.012f : AlienPitchJitter;
            _jit = 1f + UnityEngine.Random.Range(-j, j);
            _vib = ui ? 1f : WobbleAmount;
            _delay = Mathf.Max(0f, delay);

            switch (e)
            {
                // ================= MOVIMIENTO (alien / caricatura) =================

                case NodulusSoundEvent.PushHigh: Slide(72, true); break;   // Do5
                case NodulusSoundEvent.PullHigh: Slide(72, false); break;
                case NodulusSoundEvent.PushMid: Slide(67, true); break;    // Sol4
                case NodulusSoundEvent.PullMid: Slide(67, false); break;
                case NodulusSoundEvent.PushLow: Slide(60, true); break;    // Do4
                case NodulusSoundEvent.PullLow: Slide(60, false); break;

                case NodulusSoundEvent.ArcHigh: Arc(72); break;
                case NodulusSoundEvent.ArcMid: Arc(67); break;
                case NodulusSoundEvent.ArcLow: Arc(60); break;

                case NodulusSoundEvent.Rotate90:
                    // "BOING": resorte = vibrato profundo (3 semitonos) que se amortigua en 120 ms
                    Tone(SynthWave.Sine, 330f, 440f, 0.3f, 0.002f, 0.25f, 0f, 0.05f, 0.2f, 0.32f,
                         p2: 0.15f, vibR: 11f, vibD: 3f, vibDec: 0.12f);
                    Tone(SynthWave.Triangle, 1200f, 1200f, 0f, 0.001f, 0.02f, 0f, 0.005f, 0.02f, 0.1f); // clic de ataque
                    break;

                case NodulusSoundEvent.InvalidMove:
                    // "nuh-UH": dos quejidos que bajan, zumbones (FM ratio 1 = timbre de onda cuadrada) y gruñones (vibrato 25 Hz)
                    Tone(SynthWave.Sine, 300f, 260f, 0.06f, 0.003f, 0.06f, 0.3f, 0.06f, 0.04f, 0.28f,
                         fmR: 1f, fmI: 2.5f, cutoff: 1800f, vibR: 25f, vibD: 0.4f);
                    Tone(SynthWave.Sine, 240f, 160f, 0.12f, 0.003f, 0.08f, 0.3f, 0.08f, 0.08f, 0.3f,
                         at: 0.11f, fmR: 1f, fmI: 2.5f, cutoff: 1500f, vibR: 25f, vibD: 0.5f);
                    break;

                case NodulusSoundEvent.NodeEnter:
                    // "bii-BOOP!": dos chirridos que entran desde abajo (glide de -5 semitonos) formando una quinta
                    Tone(SynthWave.Wavetable, M(69) * 0.75f, M(69), 0.04f, 0.005f, 0.1f, 0.3f, 0.08f, 0.12f, 0.2f,
                         cutoff: 3500f);
                    Tone(SynthWave.Wavetable, M(76) * 0.75f, M(76), 0.04f, 0.005f, 0.15f, 0.3f, 0.12f, 0.3f, 0.18f,
                         at: 0.08f, cutoff: 3500f, vibR: 8f, vibD: 0.3f);
                    break;

                case NodulusSoundEvent.NodeLeave:
                    // "wah-waah" triste: cae 3 semitonos con un vibrato lento
                    Tone(SynthWave.Triangle, M(64), M(61), 0.2f, 0.01f, 0.1f, 0.5f, 0.15f, 0.15f, 0.18f,
                         cutoff: 1500f, vibR: 5f, vibD: 0.5f);
                    break;

                case NodulusSoundEvent.GridAppear:
                    // Platillo volador aterrizando: barrido ascendente con vibrato rápido y un soplo agudo
                    Tone(SynthWave.Sine, 300f, 900f, 0.6f, 0.2f, 0.2f, 0.6f, 0.4f, 0.3f, 0.14f,
                         p2: 0.2f, vibR: 14f, vibD: 1.5f, cutoff: 4000f);
                    Tone(SynthWave.Sine, 1f, 1f, 0f, 0.2f, 0.2f, 0.5f, 0.4f, 0.3f, 0.05f,
                         noise: 1f, hp: 3000f, cutoff: 9000f);
                    break;

                // ================= PROGRESO =================

                case NodulusSoundEvent.LevelSolved:
                    {
                        // Fanfarria alien: chirridos ascendentes (cada nota entra con glide desde una quinta abajo)
                        int[] notes = { 72, 76, 79, 84 };
                        for (int i = 0; i < notes.Length; i++)
                        {
                            bool last = i == notes.Length - 1;
                            float f = M(notes[i]);
                            Tone(SynthWave.Sine, f * 0.667f, f, 0.03f, 0.005f, 0.15f, last ? 0.4f : 0.1f,
                                 last ? 0.3f : 0.08f, last ? 0.9f : 0.15f, 0.22f,
                                 at: i * 0.1f, p2: 0.3f, p3: 0.1f,
                                 vibR: last ? 6f : 0f, vibD: last ? 0.5f : 0f);
                        }
                        // Destello final: FM inarmónica muy aguda
                        Tone(SynthWave.Sine, M(96), M(96), 0f, 0.002f, 0.3f, 0f, 0.05f, 0.4f, 0.06f,
                             at: 0.32f, fmR: 3.5f, fmI: 1.5f, fmEnd: 0f, fmT: 0.4f);
                        break;
                    }

                case NodulusSoundEvent.GameEnd:
                    // El platillo se va: barrido largo descendente con vibrato
                    Tone(SynthWave.Sine, 900f, 150f, 1.2f, 0.05f, 0.3f, 0.7f, 1f, 0.4f, 0.15f,
                         p2: 0.2f, vibR: 10f, vibD: 1f);
                    break;

                case NodulusSoundEvent.LevelEnable:
                    Tone(SynthWave.Sine, M(79), M(79), 0f, 0.003f, 0.1f, 0f, 0.05f, 0.25f, 0.15f);
                    Tone(SynthWave.Sine, M(84), M(84), 0f, 0.003f, 0.1f, 0f, 0.05f, 0.25f, 0.15f, at: 0.08f);
                    break;

                // ================= INTERFAZ (más cortos y discretos) =================

                case NodulusSoundEvent.UiConfirm: // dos tonos ascendentes, intervalo de quinta
                    Tone(SynthWave.Sine, M(72), M(72), 0f, 0.003f, 0.06f, 0f, 0.03f, 0.1f, 0.15f);
                    Tone(SynthWave.Sine, M(79), M(79), 0f, 0.003f, 0.06f, 0f, 0.03f, 0.1f, 0.15f, at: 0.06f);
                    break;

                case NodulusSoundEvent.UiBack: // tono descendente corto
                    Tone(SynthWave.Sine, 600f, 400f, 0.08f, 0.002f, 0.07f, 0f, 0.03f, 0.05f, 0.15f);
                    break;

                case NodulusSoundEvent.UiHover: // microtono < 80 ms
                    Tone(SynthWave.Sine, 1000f, 1000f, 0f, 0.002f, 0.03f, 0f, 0.01f, 0.03f, 0.08f);
                    break;

                case NodulusSoundEvent.UiPause: // pulso neutro: wavetable con low-pass cerrado
                    Tone(SynthWave.Wavetable, 220f, 220f, 0f, 0.01f, 0.1f, 0f, 0.05f, 0.15f, 0.3f, cutoff: 700f);
                    break;

                case NodulusSoundEvent.UiRestart: // ruido filtrado breve + tono base
                    Tone(SynthWave.Sine, 220f, 220f, 0f, 0.001f, 0.08f, 0f, 0.04f, 0.06f, 0.25f, noise: 0.5f, cutoff: 2500f);
                    break;
            }
        }

        private static bool IsUi(NodulusSoundEvent e)
        {
            return e >= NodulusSoundEvent.UiConfirm;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_bank == null) return;
            int frames = data.Length / channels;
            lock (_bank.Lock)
            {
                for (int i = 0; i < frames; i++)
                {
                    // soft-clip (tanh) + techo de 0.9 (~ -0.9 dBFS): sin clipping digital
                    float s = (float)System.Math.Tanh(_bank.Mix() * Volume) * 0.9f;
                    for (int c = 0; c < channels; c++) data[i * channels + c] = s;
                }
            }
        }
    }
}
