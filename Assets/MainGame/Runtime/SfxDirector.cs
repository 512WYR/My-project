using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ruin.Runtime
{
    // Every non-music sound in the game: one-shots for combat beats and a UI click shared by every
    // button. Clips live under Resources/StageOne/Audio/Sfx and are loaded on first use, so a missing
    // file only warns once and never blocks the page that asked for it.
    public enum SfxCue
    {
        Click,
        Turn,
        DefendUp,
        GuardHit,
        AttackWindup,
        AttackHit,
        AttackMiss,
        Block,
        Heal,
        SanityAttack,
        // The unsplit attack take. The cut-in uses the windup/hit pair, but scripts and previews that
        // cannot drive the two-beat animation can still ask for a single attack sound.
        Attack
    }

    public sealed class SfxDirector : MonoBehaviour
    {
        private const string ClipRoot = "StageOne/Audio/Sfx/";
        private const string VolumeKey = "ruin.sfx.volume";
        private const int Voices = 6;

        // Indexed by SfxCue. Halves of the attack are balanced so the windup does not overpower the hit.
        private static readonly string[] ClipNames =
        {
            "click", "turn", "defend_up", "guard_hit", "attack_windup", "attack_hit",
            "attack_miss", "block", "heal", "sanity_attack", "attack"
        };
        private static readonly float[] CueVolumes =
        {
            .55f, .7f, .8f, .9f, .75f, .9f,
            .8f, .9f, .8f, .9f, .9f
        };

        private static SfxDirector instance;
        private static float masterVolume = -1f;

        private readonly Dictionary<SfxCue, AudioClip> clips = new Dictionary<SfxCue, AudioClip>();
        private readonly HashSet<SfxCue> missing = new HashSet<SfxCue>();
        private readonly List<RaycastResult> raycastHits = new List<RaycastResult>(8);
        private AudioSource[] voices;
        private int nextVoice;

        // Player-facing effect level, separate from the music stem so the settings page can move them
        // independently. Persisted next to the music volume key.
        public static float MasterVolume
        {
            get
            {
                if (masterVolume < 0f) masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
                return masterVolume;
            }
        }

        public static void SetMasterVolume(float value)
        {
            masterVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumeKey, masterVolume);
        }

        public static void SaveMasterVolume() { PlayerPrefs.Save(); }

        public static SfxDirector Ensure()
        {
            if (instance) return instance;
            instance = FindObjectOfType<SfxDirector>();
            if (instance) return instance;
            // AddComponent runs Awake immediately, which builds the voice pool.
            instance = new GameObject("SfxDirector").AddComponent<SfxDirector>();
            return instance;
        }

        // Static entry point so callers never have to hold a reference. Ensure() keeps one director for
        // the whole scene; a second one would double every click.
        public static void Play(SfxCue cue, float gain = 1f)
        {
            var director = Ensure();
            if (director) director.PlayCue(cue, gain);
        }

        private void Awake()
        {
            if (!instance) instance = this;
            if (voices == null) Build();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Build()
        {
            // A handful of voices so a multi-target beat can overlap a heal and a hit without cutting
            // either short. Round-robin is enough; nothing here needs priority.
            voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                voices[i] = source;
            }
        }

        private void PlayCue(SfxCue cue, float gain)
        {
            var clip = ClipFor(cue);
            if (!clip || voices == null || voices.Length == 0) return;

            var voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            voice.Stop();
            voice.clip = clip;
            voice.volume = MasterVolume * CueVolumes[(int)cue] * Mathf.Clamp01(gain);
            voice.Play();
        }

        private AudioClip ClipFor(SfxCue cue)
        {
            AudioClip clip;
            if (clips.TryGetValue(cue, out clip)) return clip;
            if (missing.Contains(cue)) return null;

            string path = ClipRoot + ClipNames[(int)cue];
            clip = Resources.Load<AudioClip>(path);
            if (!clip)
            {
                missing.Add(cue);
                Debug.LogWarning("SFX clip missing: Resources/" + path);
                return null;
            }
            clips[cue] = clip;
            return clip;
        }

        // The click is owned here instead of by each button factory: pages are built (and rebuilt) in
        // code all over the project, and a global raycast means new buttons get the sound for free.
        // Only buttons count; the cut-in scrim and plain panels stay silent.
        private void Update()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            var events = EventSystem.current;
            if (!events || !events.IsPointerOverGameObject()) return;

            var data = new PointerEventData(events) { position = Input.mousePosition };
            raycastHits.Clear();
            events.RaycastAll(data, raycastHits);
            for (int i = 0; i < raycastHits.Count; i++)
            {
                var button = raycastHits[i].gameObject.GetComponentInParent<Button>();
                if (!button || !button.IsInteractable()) continue;
                PlayCue(SfxCue.Click, 1f);
                return;
            }
        }
    }
}
