using UnityEngine;

namespace View.Control.Procedural
{
    /// <summary>
    /// AudioSettingsPanel: panel de mezcla dentro del juego (tecla F1 o botón "Audio" en la esquina).
    /// Controla el volumen de música, movimiento e interfaz por separado, y el ducking.
    /// Muestra el progreso musical en vivo (útil para el video de demostración).
    /// Se crea por código desde GameAudio: no hay que tocar la escena.
    /// Los valores se guardan en PlayerPrefs.
    /// </summary>
    public class AudioSettingsPanel : MonoBehaviour
    {
        private const string KeyMusic = "audio.vol.music";
        private const string KeyMove = "audio.vol.move";
        private const string KeyUi = "audio.vol.ui";
        private const string KeyDuck = "audio.duck";

        public KeyCode ToggleKey = KeyCode.F1;
        public bool ShowCornerButton = true;

        private GameAudio _audio;
        private bool _open;
        private Rect _window = new Rect(20, 20, 320, 280);

        private void Awake()
        {
            _audio = GetComponent<GameAudio>();
        }

        /// <summary>Carga los volúmenes guardados. GameAudio lo llama en Start.</summary>
        public void Load()
        {
            if (_audio == null) return;
            _audio.MusicVolume = PlayerPrefs.GetFloat(KeyMusic, _audio.MusicVolume);
            _audio.MoveVolume = PlayerPrefs.GetFloat(KeyMove, _audio.MoveVolume);
            _audio.UiVolume = PlayerPrefs.GetFloat(KeyUi, _audio.UiVolume);
            _audio.DuckAmount = PlayerPrefs.GetFloat(KeyDuck, _audio.DuckAmount);
            _audio.ApplyMix();
        }

        private void Save()
        {
            PlayerPrefs.SetFloat(KeyMusic, _audio.MusicVolume);
            PlayerPrefs.SetFloat(KeyMove, _audio.MoveVolume);
            PlayerPrefs.SetFloat(KeyUi, _audio.UiVolume);
            PlayerPrefs.SetFloat(KeyDuck, _audio.DuckAmount);
            PlayerPrefs.Save();
        }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey)) _open = !_open;
        }

        private void OnGUI()
        {
            if (_audio == null) return;

            float scale = Mathf.Max(1f, Screen.height / 720f); // legible en pantallas grandes y móviles
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            if (!_open)
            {
                if (ShowCornerButton && GUI.Button(new Rect(10, 10, 70, 28), "Audio"))
                    _open = true;
                return;
            }

            _window = GUI.Window(4242, _window, DrawWindow, "Mezcla de audio procedural");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(4);

            float music = Slider("Música", _audio.MusicVolume, 0f, 1f);
            float move = Slider("Movimiento (tablero)", _audio.MoveVolume, 0f, 1f);
            float ui = Slider("Interfaz", _audio.UiVolume, 0f, 1f);
            float duck = Slider("Ducking de la música", _audio.DuckAmount, 0f, 0.8f);

            bool changed = !Mathf.Approximately(music, _audio.MusicVolume) ||
                           !Mathf.Approximately(move, _audio.MoveVolume) ||
                           !Mathf.Approximately(ui, _audio.UiVolume) ||
                           !Mathf.Approximately(duck, _audio.DuckAmount);
            if (changed)
            {
                _audio.MusicVolume = music;
                _audio.MoveVolume = move;
                _audio.UiVolume = ui;
                _audio.DuckAmount = duck;
                _audio.ApplyMix();
                Save();
            }

            GUILayout.Space(6);
            GUILayout.Label($"Progreso musical: {_audio.CurrentProgress:0.00}   " +
                            $"(jugadas: {_audio.MovesThisLevel})");
            GUILayout.Label($"Música: {(_audio.MusicEnabled ? "activa" : "silenciada")}   " +
                            $"Efectos: {(_audio.SfxEnabled ? "activos" : "silenciados")}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Probar UI")) _audio.Trigger(NodulusSoundEvent.UiConfirm);
            if (GUILayout.Button("Probar error")) _audio.Trigger(NodulusSoundEvent.InvalidMove);
            if (GUILayout.Button("Cerrar")) _open = false;
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        private static float Slider(string label, float value, float min, float max)
        {
            float db = 20f * Mathf.Log10(Mathf.Max(value, 0.0001f));
            GUILayout.Label($"{label}: {value:0.00}  ({(value <= 0.0001f ? "-inf" : db.ToString("0.0"))} dB)");
            return GUILayout.HorizontalSlider(value, min, max);
        }
    }
}
