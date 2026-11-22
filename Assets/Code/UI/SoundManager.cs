using UnityEngine;

public class SoundManager : MonoBehaviour
{
    // ตัวแปรกลางให้สคริปต์อื่นเรียกใช้ได้ทันทีผ่าน SoundManager.Instance
    public static SoundManager Instance { get; private set; }

    [Header("Audio Sources (ลากลูกทั้ง 2 ตัวมาใส่)")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource sfxSource;

    [Header("Volume Settings (ระดับความดัง)")]
    [Range(0f, 1f)][SerializeField] private float bgmVolume = 0.5f;
    [Range(0f, 1f)][SerializeField] private float sfxVolume = 0.8f;

    [Header("Audio Clips (ลากไฟล์เสียง .wav / .mp3 มาใส่)")]
    public AudioClip bgmClip;
    public AudioClip skillUseSFX;
    public AudioClip takeDamageSFX;
    public AudioClip healSFX;
    public AudioClip expGainSFX;
    public AudioClip buttonClickSFX;

    [Header("Sandbox Testing (สร้างเสียงจำลองถ้ายังไม่มีไฟล์เสียง)")]
    [SerializeField] private bool playTestToneIfClipMissing = true;

    private void Awake()
    {
        // ตั้งค่า Singleton ให้มี SoundManager ตัวเดียวในเกมและไม่ถูกทำลายเมื่อเปลี่ยนฉาก
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        UpdateVolumes();

        // ถ้าใส่เพลง BGM ไว้ ให้เริ่มเล่นอัตโนมัติทันทีที่เข้าเกม
        if (bgmClip != null)
        {
            PlayBGM(bgmClip);
        }
    }

    // ฟังก์ชันเล่นเพลงพื้นหลัง
    public void PlayBGM(AudioClip clip)
    {
        if (bgmSource == null || clip == null) return;

        bgmSource.clip = clip;
        bgmSource.volume = bgmVolume;
        bgmSource.loop = true;
        bgmSource.Play();
    }

    // ฟังก์ชันเล่นเสียงเอฟเฟกต์ (เล่นซ้อนกันได้หลายเสียงโดยไม่ตัดกัน)
    public void PlaySFX(AudioClip clip, float fallbackFrequency = 440f)
    {
        if (sfxSource == null) return;

        if (clip != null)
        {
            sfxSource.PlayOneShot(clip, sfxVolume);
        }
        else if (playTestToneIfClipMissing)
        {
            // หากยังไม่มีไฟล์เสียงจากฝ่าย Art จะสร้างเสียงติ๊ดจำลองตามความถี่ให้ฟังทดสอบแทน
            AudioClip tempTone = CreateTestTone(fallbackFrequency, 0.12f);
            sfxSource.PlayOneShot(tempTone, sfxVolume * 0.4f);
        }
    }

    // --- ฟังก์ชันลัดสำหรับเรียกใช้งานแต่ละเหตุการณ์ ---
    public void PlaySkillSFX() => PlaySFX(skillUseSFX, 520f);       // เสียงกดใช้สกิล (โทนกลาง)
    public void PlayDamageSFX() => PlaySFX(takeDamageSFX, 180f);    // เสียงโดนโจมตี (โทนต่ำทุ้ม)
    public void PlayHealSFX() => PlaySFX(healSFX, 700f);            // เสียงฮีลเลือด (โทนสูงใส)
    public void PlayExpSFX() => PlaySFX(expGainSFX, 880f);          // เสียงเก็บ EXP (โทนสูงพิเศษ)
    public void PlayClickSFX() => PlaySFX(buttonClickSFX, 600f);    // เสียงคลิกปุ่ม UI

    public void UpdateVolumes()
    {
        if (bgmSource != null) bgmSource.volume = bgmVolume;
        if (sfxSource != null) sfxSource.volume = sfxVolume;
    }

    // สร้างคลื่นเสียงสั้นๆ สำหรับทดสอบระบบตอนยังไม่มีไฟล์จริง
    private AudioClip CreateTestTone(float frequency, float duration)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = 1f - ((float)i / sampleCount); // ให้เสียงค่อยๆ เบาลงท้ายคลิป
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope;
        }

        AudioClip clip = AudioClip.Create("TempTone", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}