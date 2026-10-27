using UnityEngine;

/// <summary>
/// Creates the sound effects in code, so the project needs no audio files.
/// </summary>
public static class SoundMaker
{
    const int SampleRate = 44100;

    static AudioClip shot;
    static AudioClip hit;

    // "Bang": random noise that fades out quickly
    public static AudioClip Shot
    {
        get
        {
            if (shot == null)
                shot = Create("Shot", 0.15f, (time, fade) => Random.Range(-1f, 1f) * fade * fade * fade * 0.8f);
            return shot;
        }
    }

    // "Ding": a high sine tone that fades out
    public static AudioClip Hit
    {
        get
        {
            if (hit == null)
                hit = Create("Hit", 0.2f, (time, fade) => Mathf.Sin(2f * Mathf.PI * 1320f * time) * fade * fade * 0.5f);
            return hit;
        }
    }

    // wave(time in seconds, fade from 1 to 0) returns one sample between -1 and 1
    static AudioClip Create(string name, float seconds, System.Func<float, float, float> wave)
    {
        int length = Mathf.RoundToInt(SampleRate * seconds);
        var samples = new float[length];

        for (int i = 0; i < length; i++)
        {
            float time = (float)i / SampleRate;
            float fade = 1f - (float)i / length;
            samples[i] = wave(time, fade);
        }

        AudioClip clip = AudioClip.Create(name, length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
