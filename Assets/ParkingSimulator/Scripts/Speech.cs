using System.Diagnostics;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>
    /// カーナビの音声読み上げ。OS の音声合成を使う(macOS: say コマンドの日本語音声 Kyoko、Windows: System.Speech)。
    /// 新しい案内を話すときは、話し途中の案内を止める
    /// </summary>
    public static class Speech
    {
        static Process current;

        public static void Say(string text, float volume)
        {
            Stop();
            if (volume <= 0.01f || string.IsNullOrEmpty(text)) return;
            try
            {
                var psi = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
                switch (Application.platform)
                {
                    case RuntimePlatform.OSXPlayer:
                    case RuntimePlatform.OSXEditor:
                        psi.FileName = "/usr/bin/say";
                        psi.Arguments = $"-v Kyoko -r 190 \"[[volm {Mathf.Clamp01(volume):0.00}]] {Escape(text)}\"";
                        break;
                    case RuntimePlatform.WindowsPlayer:
                    case RuntimePlatform.WindowsEditor:
                        psi.FileName = "powershell";
                        psi.Arguments = "-NoProfile -Command \"Add-Type -AssemblyName System.Speech; " +
                                        "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                                        $"$s.Volume = {Mathf.RoundToInt(Mathf.Clamp01(volume) * 100)}; $s.Speak('{text.Replace("'", "")}')\"";
                        break;
                    default:
                        return;
                }
                current = Process.Start(psi);
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"音声案内を再生できませんでした: {e.Message}");
            }
        }

        public static void Stop()
        {
            try
            {
                if (current != null && !current.HasExited) current.Kill();
            }
            catch { /* すでに終わっている */ }
            current = null;
        }

        static string Escape(string s) => s.Replace("\\", "").Replace("\"", "");
    }
}
