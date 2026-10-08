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
        // Interfaz
        UiConfirm, UiBack, UiHover, UiPause, UiRestart
    }

    [RequireComponent(typeof(AudioSource))]
    public class ProceduralSfx : MonoBehaviour
    {
        [Range(0f, 1f)] public float Volume = 0.8f;

        private VoiceBank _bank;
        // Contexto del disparo actual (solo hilo principal)
        private float _gain = 1f, _jit = 1f, _delay;

        private void Awake()
        {
            _bank = new VoiceBank(16, AudioSettings.outputSampleRate);
            ProcUtil.PrepareSource(GetComponent<AudioSource>());
        }

        private static float M(float midi) { return ProcUtil.Midi(midi); }

        private void Tone(SynthWave w, float f0, float f1, float glide,
                          float a, float d, float s, float hold, float r, float amp,
                          float at = 0f, float p2 = 0f, float p3 = 0f,
                          float fmR = 0f, float fmI = 0f, float noise = 0f,
                          float cutoff = 0f, float lfoR = 0f, float lfoD = 0f)
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
                delay = _delay + at
            });
        }

        /// <summary>Empuje/tirón de varilla: glide ascendente (push) o descendente (pull).</summary>
        private void Slide(float midi, bool push)
        {
            float f = M(midi), up = f * 1.122f; // +2 semitonos
            Tone(SynthWave.Triangle, push ? f : up, push ? up : f, 0.09f,
                 0.004f, 0.09f, 0f, 0.05f, 0.07f, 0.28f, p2: 0.25f, cutoff: 3500f);
        }

        /// <summary>Movimiento en arco: FM ligera + vibrato (LFO) + glide.</summary>
        private void Arc(float midi)
        {
            float f = M(midi);
            Tone(SynthWave.Sine, f, f * 1.189f, 0.2f,
                 0.01f, 0.12f, 0.25f, 0.14f, 0.12f, 0.25f, fmR: 2f, fmI: 1.2f, lfoR: 12f, lfoD: 0.25f);
        }

        public void Trigger(NodulusSoundEvent e, float intensity = 1f, float delay = 0f)
        {
            if (_bank == null) return;
            _gain = Mathf.Clamp01(intensity);
            _jit = 1f + Random.Range(-0.012f, 0.012f); // ±~20 cents: evita repetición mecánica
            _delay = Mathf.Max(0f, delay);

            switch (e)
            {
                // ---------- MOVIMIENTO ----------
                case NodulusSoundEvent.Rotate90:
                    // pulso con barrido ascendente + clic tonal aditivo al terminar
                    Tone(SynthWave.Triangle, 300f, 520f, 0.05f, 0.003f, 0.05f, 0f, 0.03f, 0.04f, 0.35f);
                    Tone(SynthWave.Sine, M(76), M(76), 0f, 0.002f, 0.09f, 0f, 0.04f, 0.1f, 0.3f,
                         at: 0.12f, p2: 0.4f, p3: 0.2f);
                    break;

                case NodulusSoundEvent.InvalidMove:
                    // seco, descendente, poco brillo: seno + FM ligera + low-pass
                    Tone(SynthWave.Sine, 260f, 150f, 0.12f, 0.002f, 0.1f, 0f, 0.05f, 0.06f, 0.4f,
                         fmR: 0.5f, fmI: 2f, cutoff: 1200f);
                    break;

                case NodulusSoundEvent.NodeEnter: // conexión: quinta consonante, release más largo
                    Tone(SynthWave.Sine, M(69), M(69), 0f, 0.01f, 0.15f, 0.3f, 0.15f, 0.35f, 0.22f, p2: 0.3f);
                    Tone(SynthWave.Sine, M(76), M(76), 0f, 0.01f, 0.15f, 0.3f, 0.15f, 0.35f, 0.18f, at: 0.03f, p2: 0.2f);
                    break;

                case NodulusSoundEvent.NodeLeave:
                    Tone(SynthWave.Triangle, M(64), M(64) * 0.94f, 0.08f, 0.005f, 0.08f, 0f, 0.04f, 0.12f, 0.2f, cutoff: 2500f);
                    break;

                case NodulusSoundEvent.PushHigh: Slide(69, true); break;
                case NodulusSoundEvent.PullHigh: Slide(69, false); break;
                case NodulusSoundEvent.PushMid: Slide(64, true); break;
                case NodulusSoundEvent.PullMid: Slide(64, false); break;
                case NodulusSoundEvent.PushLow: Slide(57, true); break;
                case NodulusSoundEvent.PullLow: Slide(57, false); break;

                case NodulusSoundEvent.ArcHigh: Arc(69); break;
                case NodulusSoundEvent.ArcMid: Arc(64); break;
                case NodulusSoundEvent.ArcLow: Arc(57); break;

                // ---------- PROGRESO ----------
                case NodulusSoundEvent.LevelSolved:
                    {
                        // Arpegio ascendente en pentatónica de Do mayor (mismas notas que la música: La menor pent.)
                        int[] notes = { 72, 76, 79, 81, 84 };
                        for (int i = 0; i < notes.Length; i++)
                        {
                            bool last = i == notes.Length - 1;
                            Tone(SynthWave.Sine, M(notes[i]), M(notes[i]), 0f, 0.005f, 0.2f, 0.1f, 0.12f,
                                 last ? 0.9f : 0.4f, 0.22f, at: i * 0.11f + (last ? 0.03f : 0f), p2: 0.3f, p3: 0.1f);
                        }
                        break;
                    }

                case NodulusSoundEvent.GridAppear:
                    Tone(SynthWave.Sine, 400f, 1200f, 0.5f, 0.15f, 0.3f, 0f, 0.15f, 0.3f, 0.18f,
                         p2: 0.3f, p3: 0.2f, noise: 0.03f, cutoff: 4000f);
                    break;

                case NodulusSoundEvent.GameEnd:
                    {
                        int[] notes = { 76, 72, 69, 57 };
                        for (int i = 0; i < notes.Length; i++)
                            Tone(SynthWave.Sine, M(notes[i]), M(notes[i]), 0f, 0.01f, 0.4f, 0.1f, 0.2f, 0.9f, 0.2f,
                                 at: i * 0.25f, p2: 0.3f);
                        break;
                    }

                case NodulusSoundEvent.LevelEnable:
                    Tone(SynthWave.Sine, M(79), M(79), 0f, 0.003f, 0.1f, 0f, 0.05f, 0.25f, 0.15f);
                    Tone(SynthWave.Sine, M(84), M(84), 0f, 0.003f, 0.1f, 0f, 0.05f, 0.25f, 0.15f, at: 0.08f);
                    break;

                // ---------- INTERFAZ (más cortos y discretos) ----------
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

                case NodulusSoundEvent.UiPause: // pulso neutro con filtro cerrado
                    Tone(SynthWave.Triangle, 220f, 220f, 0f, 0.01f, 0.1f, 0f, 0.05f, 0.15f, 0.25f, p2: 0.3f, cutoff: 800f);
                    break;

                case NodulusSoundEvent.UiRestart: // ruido filtrado breve + tono base
                    Tone(SynthWave.Sine, 220f, 220f, 0f, 0.001f, 0.08f, 0f, 0.04f, 0.06f, 0.25f, noise: 0.5f, cutoff: 2500f);
                    break;
            }
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