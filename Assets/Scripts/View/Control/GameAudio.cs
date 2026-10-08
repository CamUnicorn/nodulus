using UnityEngine;
using View.Control.Procedural;

namespace View.Control
{
    /// <summary>
    /// Controlador de audio del juego. Misma API pública que el original, pero ahora
    /// los sonidos se SINTETIZAN (ver carpeta Procedural) en vez de reproducir AudioClips.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        private const string MusicStatusKey = "music.status";
        private const string SfxStatusKey = "sfx.status";

        private int _moves;
        private const float MovesForFullEnergy = 20f; // ajusta: más alto = la música tarda más en "llenarse"

        private const float MusicVolume = 0.6f; // música por debajo de los efectos
        private const float SfxVolume = 0.8f;

        // Se conservan para no romper la escena/inspector. Ya no se usan.
        public AudioClip[] MusicClips;
        public AudioClip[] SfxClips;

        private ProceduralSfx _sfx;
        private ProceduralMusic _music;

        private bool _musicEnabled = true;
        public bool MusicEnabled
        {
            get { return _musicEnabled; }
            set
            {
                _musicEnabled = value;
                if (_music != null) _music.FadeTo(value ? MusicVolume : 0f, 0.3f); // solo la música
                PlayerPrefs.SetInt(MusicStatusKey, value ? 0 : 1);
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
            }
        }

        private void Awake()
        {
            // Hijos creados por código: no hay que tocar la escena ni los prefabs.
            _sfx = new GameObject("ProceduralSfx").AddComponent<ProceduralSfx>();
            _sfx.transform.SetParent(transform, false);
            _sfx.Volume = SfxVolume;

            _music = new GameObject("ProceduralMusic").AddComponent<ProceduralMusic>();
            _music.transform.SetParent(transform, false);
        }

        private void Start()
        {
            if (!PlayerPrefs.HasKey(MusicStatusKey)) PlayerPrefs.SetInt(MusicStatusKey, 0);
            if (!PlayerPrefs.HasKey(SfxStatusKey)) PlayerPrefs.SetInt(SfxStatusKey, 0);

            _musicEnabled = PlayerPrefs.GetInt(MusicStatusKey) == 0;
            _sfxEnabled = PlayerPrefs.GetInt(SfxStatusKey) == 0;

            StartMusic();
        }

        /// <summary>Dispara un evento sonoro procedural directamente (útil para botones de UI).</summary>
        public void Trigger(NodulusSoundEvent e, float intensity = 1f, float delay = 0f)
        {
            if (!enabled || !SfxEnabled || _sfx == null) return;
            _sfx.Trigger(e, intensity, delay);
        }

        /// <summary>0 = inicio del nivel, 1 = cerca de la solución. Sube densidad/brillo de la música.</summary>
        public void SetProgress(float progress)
        {
            if (_music != null) _music.SetProgress(progress);
        }
        // Deduce el progreso del nivel a partir de los sonidos que el juego ya pide
        private void UpdateProgress(GameClip clip)
        {
            switch (clip)
            {
                case GameClip.GameStart:      // empieza el tablero
                    _moves = 0;
                    SetProgress(0f);
                    break;

                case GameClip.WinBoard:       // nivel resuelto
                    SetProgress(1f);
                    break;

                case GameClip.NodeRotate90:   // cada movimiento del jugador suma energía
                case GameClip.MovePushHigh:
                case GameClip.MovePullHigh:
                case GameClip.MovePushMid:
                case GameClip.MovePullMid:
                case GameClip.MovePushLow:
                case GameClip.MovePullLow:
                case GameClip.ArcMoveHigh:
                case GameClip.ArcMoveMid:
                case GameClip.ArcMoveLow:
                    _moves++;
                    SetProgress(_moves / MovesForFullEnergy);
                    break;
            }
        }

        /// <summary>Misma firma que el original: traduce el GameClip a un evento procedural.</summary>
        public void Play(GameClip clip, float delay = 0f, float volume = 1f, float startTime = 0f)
        {
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