using UnityEngine;
using View.Control.Procedural;

namespace View.Control
{
    /// <summary>
    /// ProceduralAudioManager del juego. Conserva la API pública del GameAudio original
    /// (Play(GameClip), Play(MusicClip), MusicEnabled, SfxEnabled) para no tocar escenas ni prefabs,
    /// pero ahora los sonidos se SINTETIZAN en tiempo real (ver carpeta Procedural).
    ///
    /// Responsabilidades:
    ///  - Traducir los GameClip que ya pide el juego a eventos procedurales (SoundEventMap).
    ///  - Deducir el progreso del nivel y pasarlo a la música generativa.
    ///  - Mezcla: volumen por familia (movimiento / UI / música) y ducking de la música.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        private const string MusicStatusKey = "music.status";
        private const string SfxStatusKey = "sfx.status";

        [Header("Mezcla (amplitud lineal 0-1)")]
        [Tooltip("Volumen de la música generativa. Queda por debajo de los efectos.")]
        [Range(0f, 1f)] public float MusicVolume = 0.55f;
        [Tooltip("Volumen de los sonidos de tablero (movimiento, conexión, victoria).")]
        [Range(0f, 1f)] public float MoveVolume = 0.8f;
        [Tooltip("Volumen de los sonidos de interfaz (más discretos que el tablero).")]
        [Range(0f, 1f)] public float UiVolume = 0.6f;

        [Header("Ducking: la música baja cuando suena un efecto")]
        [Range(0f, 1f)] public float DuckAmount = 0.35f;   // 0.35 ≈ -3.7 dB
        public float DuckRelease = 0.6f;                    // segundos para recuperarse

        [Header("Respuesta al progreso")]
        [Tooltip("Movimientos necesarios para llevar la música a energía máxima.")]
        public float MovesForFullEnergy = 12f;

        // Se conservan para no romper la escena/inspector. Ya no se usan.
        public AudioClip[] MusicClips;
        public AudioClip[] SfxClips;

        private ProceduralSfx _sfx;
        private ProceduralMusic _music;
        private int _moves;
        private float _progress;
        private AudioSettingsPanel _panel;

        /// <summary>Progreso musical actual (0-1). Lo muestra el AudioSettingsPanel.</summary>
        public float CurrentProgress => _progress;
        /// <summary>Jugadas reales hechas en el nivel actual.</summary>
        public int MovesThisLevel => _moves;

        private bool _musicEnabled = true;
        public bool MusicEnabled
        {
            get { return _musicEnabled; }
            set
            {
                _musicEnabled = value;
                if (_music != null) _music.FadeTo(value ? MusicVolume : 0f, 0.3f); // solo la música
                PlayerPrefs.SetInt(MusicStatusKey, value ? 0 : 1);
                // Feedback de UI: los efectos siguen sonando aunque la música se apague
                Trigger(value ? NodulusSoundEvent.UiConfirm : NodulusSoundEvent.UiBack, 0.8f);
            }
        }

        private bool _sfxEnabled = true;
        public bool SfxEnabled
        {
            get { return _sfxEnabled; }
            set
            {
                _sfxEnabled = value;
                PlayerPrefs.SetInt(SfxStatusKey, value ? 0 : 1);
                if (value) Trigger(NodulusSoundEvent.UiConfirm, 0.8f); // confirma que volvieron los efectos
            }
        }

        private void Awake()
        {
            // Hijos creados por código: no hay que tocar la escena ni los prefabs.
            _sfx = new GameObject("ProceduralSfx").AddComponent<ProceduralSfx>();
            _sfx.transform.SetParent(transform, false);

            _music = new GameObject("ProceduralMusic").AddComponent<ProceduralMusic>();
            _music.transform.SetParent(transform, false);

            // Panel de mezcla (F1 o botón "Audio"): volumen por familia en el juego
            _panel = gameObject.AddComponent<AudioSettingsPanel>();

            ApplyMix();
        }

        private void Start()
        {
            if (!PlayerPrefs.HasKey(MusicStatusKey)) PlayerPrefs.SetInt(MusicStatusKey, 0);
            if (!PlayerPrefs.HasKey(SfxStatusKey)) PlayerPrefs.SetInt(SfxStatusKey, 0);

            _musicEnabled = PlayerPrefs.GetInt(MusicStatusKey) == 0;
            _sfxEnabled = PlayerPrefs.GetInt(SfxStatusKey) == 0;

            if (_panel != null) _panel.Load(); // volúmenes guardados por el jugador
            StartMusic();
        }

        // Permite ajustar la mezcla en vivo desde el Inspector mientras se juega
        private void OnValidate() { ApplyMix(); }

        /// <summary>Aplica los volúmenes actuales a los módulos de síntesis.</summary>
        public void ApplyMix()
        {
            if (_sfx != null)
            {
                _sfx.Volume = 1f;             // bus maestro: el limitador tanh queda igual
                _sfx.MoveGain = MoveVolume;   // familias independientes: bajar una no toca la otra
                _sfx.UiVolume = UiVolume;
            }
            if (_music != null && _musicEnabled && UnityEngine.Application.isPlaying) _music.FadeTo(MusicVolume, 0.2f);
        }

        /// <summary>Dispara un evento sonoro procedural directamente (útil para botones de UI).</summary>
        public void Trigger(NodulusSoundEvent e, float intensity = 1f, float delay = 0f)
        {
            if (!enabled || !SfxEnabled || _sfx == null) return;
            _sfx.Trigger(e, intensity, delay);
            if (_music != null) _music.Duck(DuckAmount * Mathf.Clamp01(intensity), DuckRelease, delay);
        }

        /// <summary>0 = inicio del nivel, 1 = cerca de la solución. Sube densidad/brillo de la música.</summary>
        public void SetProgress(float progress)
        {
            _progress = Mathf.Clamp01(progress);
            if (_music != null) _music.SetProgress(_progress);
        }

        // Deduce el progreso del nivel a partir de los sonidos que el juego ya pide
        private void UpdateProgress(GameClip clip)
        {
            switch (clip)
            {
                case GameClip.GameStart:      // aparece un tablero nuevo (o se reinicia)
                    _moves = 0;
                    SetProgress(0f);
                    break;

                case GameClip.WinBoard:       // nivel resuelto: cadencia musical
                    SetProgress(1f);
                    if (_music != null && MusicEnabled) _music.Resolve();
                    break;

                // Cada jugada real (empujar o tirar una varilla) suma energía.
                // NodeRotate90 no cuenta: en Nodulus es un giro decorativo cuando no hubo jugada.
                case GameClip.MovePushHigh:
                case GameClip.MovePullHigh:
                case GameClip.MovePushMid:
                case GameClip.MovePullMid:
                case GameClip.MovePushLow:
                case GameClip.MovePullLow:
                    _moves++;
                    SetProgress(_moves / MovesForFullEnergy);
                    break;
            }
        }

        /// <summary>Misma firma que el original: traduce el GameClip a un evento procedural.</summary>
        public void Play(GameClip clip, float delay = 0f, float volume = 1f, float startTime = 0f)
        {
            UpdateProgress(clip);
            Trigger(Map(clip), volume, delay);
        }

        /// <summary>Misma firma que el original: arranca la música generativa con fade-in.</summary>
        public void Play(MusicClip clip, float fadeTime = 0f, float delay = 0f, float volume = 1f, float startTime = 0f)
        {
            if (!enabled || !MusicEnabled || _music == null) return;
            _music.FadeTo(volume, fadeTime);
        }

        private void StartMusic()
        {
            const float fadeTime = 3f;
            Play(MusicClip.Ambient02, fadeTime: fadeTime, volume: MusicVolume);
        }

        // SoundEventMap: GameClip original -> evento procedural
        private static NodulusSoundEvent Map(GameClip c)
        {
            switch (c)
            {
                case GameClip.GameStart: return NodulusSoundEvent.GridAppear;
                case GameClip.WinBoard: return NodulusSoundEvent.LevelSolved;
                case GameClip.NodeEnter: return NodulusSoundEvent.NodeEnter;
                case GameClip.NodeLeave: return NodulusSoundEvent.NodeLeave;
                case GameClip.MovePushHigh: return NodulusSoundEvent.PushHigh;
                case GameClip.MovePullHigh: return NodulusSoundEvent.PullHigh;
                case GameClip.MovePullMid: return NodulusSoundEvent.PullMid;
                case GameClip.MovePushMid: return NodulusSoundEvent.PushMid;
                case GameClip.MovePullLow: return NodulusSoundEvent.PullLow;
                case GameClip.MovePushLow: return NodulusSoundEvent.PushLow;
                case GameClip.ArcMoveHigh: return NodulusSoundEvent.ArcHigh;
                case GameClip.ArcMoveMid: return NodulusSoundEvent.ArcMid;
                case GameClip.ArcMoveLow: return NodulusSoundEvent.ArcLow;
                case GameClip.NodeRotate90: return NodulusSoundEvent.Rotate90;
                case GameClip.InvalidRotate: return NodulusSoundEvent.InvalidMove;
                case GameClip.MenuSelect: return NodulusSoundEvent.UiConfirm;
                case GameClip.GameEnd: return NodulusSoundEvent.GameEnd;
                case GameClip.LevelEnable: return NodulusSoundEvent.LevelEnable;
                default: return NodulusSoundEvent.UiHover;
            }
        }
    }

    /// <summary>A one-to-one map of all sound clips (sin cambios: otros scripts dependen de este enum)</summary>
    public enum GameClip
    {
        GameStart,
        WinBoard,
        NodeEnter,
        NodeLeave,
        MovePushHigh,
        MovePullHigh,
        MovePullMid,
        MovePushMid,
        MovePullLow,
        MovePushLow,
        ArcMoveHigh,
        NodeRotate90,
        InvalidRotate,
        MenuSelect,
        GameEnd,
        LevelEnable,
        ArcMoveMid,
        ArcMoveLow
    }

    /// <summary>A one-to-one map of all music clips (sin cambios)</summary>
    public enum MusicClip
    {
        Ambient01,
        Ambient02
    }
}
