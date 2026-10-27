// Writes every script compilation result to Logs/arena-compile.log inside the project,
// so compile errors can be read without copying them from the Console by hand.
// Lives in its own editor-only assembly, so it keeps working even when game code fails to compile.
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;

namespace Arena.EditorTools
{
    [InitializeOnLoad]
    static class ArenaCompileLog
    {
        static readonly StringBuilder Buffer = new StringBuilder();
        static string LogPath => Path.Combine(Directory.GetCurrentDirectory(), "Logs", "arena-compile.log");

        static ArenaCompileLog()
        {
            CompilationPipeline.compilationStarted -= OnStarted;
            CompilationPipeline.compilationStarted += OnStarted;
            CompilationPipeline.assemblyCompilationFinished -= OnAssembly;
            CompilationPipeline.assemblyCompilationFinished += OnAssembly;
            CompilationPipeline.compilationFinished -= OnFinished;
            CompilationPipeline.compilationFinished += OnFinished;
        }

        static void OnStarted(object _)
        {
            Buffer.Clear();
            Buffer.AppendLine("compile started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        static void OnAssembly(string assembly, CompilerMessage[] messages)
        {
            int errors = 0;
            foreach (var m in messages)
            {
                if (m.type != CompilerMessageType.Error && m.type != CompilerMessageType.Warning) continue;
                if (m.type == CompilerMessageType.Error) errors++;
                // skip warnings from Unity packages, keep ours
                if (m.type == CompilerMessageType.Warning && (m.file == null || !m.file.Replace('\\', '/').Contains("Assets/Arena"))) continue;
                Buffer.AppendLine((m.type == CompilerMessageType.Error ? "ERROR " : "warn  ") + m.file + "(" + m.line + "," + m.column + "): " + m.message);
            }
            Buffer.AppendLine((errors == 0 ? "ok    " : "FAIL  ") + Path.GetFileName(assembly) + " errors=" + errors);
        }

        static void OnFinished(object _)
        {
            Buffer.AppendLine("compile finished " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.WriteAllText(LogPath, Buffer.ToString());
            }
            catch (Exception) { /* logging must never break the editor */ }
        }
    }
}
