using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Boreal.Animals.Editor
{
    [InitializeOnLoad]
    public static class RunRevisionStamp
    {
        static RunRevisionStamp() => SimulationRunRecorder.RevisionProvider = ReadRevision;

        static string ReadRevision()
        {
            try
            {
                string revision = Git("rev-parse HEAD");
                if (string.IsNullOrEmpty(revision)) return "unrecorded";
                return revision + (string.IsNullOrEmpty(Git("status --porcelain")) ? "" : "+dirty");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("Could not stamp the recording revision: " + e.Message);
                return "unrecorded";
            }
        }

        static string Git(string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = Path.GetDirectoryName(Application.dataPath), UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            }))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(3000)) { process.Kill(); throw new IOException("Git timed out."); }
                if (process.ExitCode != 0) throw new IOException("Git could not read the project revision.");
                return output.GetAwaiter().GetResult().Trim();
            }
        }
    }
}
