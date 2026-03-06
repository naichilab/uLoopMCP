using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor.Compilation;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// UnityEditor.Compilation.AssemblyBuilder を利用した動的コードコンパイル実装
    /// Microsoft.CodeAnalysis.CSharp (Roslyn) への依存なしで動作する
    /// </summary>
    internal class UnityAssemblyBuilderCompilationService : IDynamicCompilationService
    {
        private const string TEMP_DIR = "Temp/uLoopMCPEval";

        private readonly DynamicCodeSecurityLevel _securityLevel;

        public UnityAssemblyBuilderCompilationService(DynamicCodeSecurityLevel securityLevel)
        {
            _securityLevel = securityLevel;
        }

        public async Task<CompilationResult> CompileAsync(CompilationRequest request, CancellationToken ct = default)
        {
            // Restrictedモード: テキストベースのセキュリティチェック
            if (_securityLevel == DynamicCodeSecurityLevel.Restricted)
            {
                List<SecurityViolation> violations = TextBasedDangerousApiChecker.Check(request.Code);
                if (violations.Count > 0)
                {
                    return new CompilationResult
                    {
                        Success = false,
                        HasSecurityViolations = true,
                        SecurityViolations = violations,
                        FailureReason = CompilationFailureReason.SecurityViolation
                    };
                }
            }

            string className = string.IsNullOrEmpty(request.ClassName)
                ? DynamicCodeConstants.DEFAULT_CLASS_NAME
                : request.ClassName;

            string wrappedCode = WrapCode(request.Code, className, request.Namespace ?? DynamicCodeConstants.DEFAULT_NAMESPACE);

            Directory.CreateDirectory(TEMP_DIR);
            string uniqueId = Guid.NewGuid().ToString("N");
            string sourcePath = Path.Combine(TEMP_DIR, $"DynamicCode_{uniqueId}.cs");
            string dllPath = Path.ChangeExtension(sourcePath, ".dll");

            try
            {
                File.WriteAllText(sourcePath, wrappedCode, Encoding.UTF8);

                string[] references = GetAssemblyReferences();

                TaskCompletionSource<CompilationResult> tcs = new TaskCompletionSource<CompilationResult>();

                AssemblyBuilder builder = new AssemblyBuilder(dllPath, new[] { sourcePath });
                builder.additionalReferences = references;

                builder.buildFinished += (path, messages) =>
                {
                    if (ct.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                        return;
                    }
                    CompilationResult result = BuildResult(messages, path, wrappedCode);
                    tcs.TrySetResult(result);
                };

                if (!builder.Build())
                {
                    return new CompilationResult
                    {
                        Success = false,
                        Errors = new List<CompilationError>
                        {
                            new CompilationError
                            {
                                Message = "AssemblyBuilder.Build() returned false. Another compilation may be in progress."
                            }
                        },
                        FailureReason = CompilationFailureReason.CompilationError,
                        UpdatedCode = wrappedCode
                    };
                }

                return await tcs.Task;
            }
            finally
            {
                DeleteIfExists(sourcePath);
                DeleteIfExists(dllPath);
                DeleteIfExists(Path.ChangeExtension(dllPath, ".pdb"));
            }
        }

        private static CompilationResult BuildResult(CompilerMessage[] messages, string dllPath, string wrappedCode)
        {
            List<CompilationError> errors = new List<CompilationError>();
            foreach (CompilerMessage msg in messages)
            {
                if (msg.type == CompilerMessageType.Error)
                {
                    errors.Add(ParseCompilerMessage(msg));
                }
            }

            if (errors.Count > 0)
            {
                return new CompilationResult
                {
                    Success = false,
                    Errors = errors,
                    FailureReason = CompilationFailureReason.CompilationError,
                    UpdatedCode = wrappedCode
                };
            }

            if (!File.Exists(dllPath))
            {
                return new CompilationResult
                {
                    Success = false,
                    Errors = new List<CompilationError>
                    {
                        new CompilationError { Message = $"Compilation succeeded but DLL not found: {dllPath}" }
                    },
                    FailureReason = CompilationFailureReason.DynamicAssemblyFailed,
                    UpdatedCode = wrappedCode
                };
            }

            try
            {
                System.Reflection.Assembly assembly = LoadAssembly(dllPath);
                return new CompilationResult
                {
                    Success = true,
                    CompiledAssembly = assembly,
                    UpdatedCode = wrappedCode
                };
            }
            catch (Exception ex)
            {
                return new CompilationResult
                {
                    Success = false,
                    Errors = new List<CompilationError>
                    {
                        new CompilationError { Message = $"Failed to load compiled assembly: {ex.Message}" }
                    },
                    FailureReason = CompilationFailureReason.DynamicAssemblyFailed,
                    UpdatedCode = wrappedCode
                };
            }
        }

        private static CompilationError ParseCompilerMessage(CompilerMessage msg)
        {
            string rawMessage = msg.message ?? string.Empty;
            string errorCode = string.Empty;
            string cleanMessage = rawMessage;

            Match match = Regex.Match(rawMessage, @"\b(CS\d{4})\b");
            if (match.Success)
            {
                errorCode = match.Value;
                // "Assets/.../file.cs(10,5): error CS0246: ..." → "The type..." だけを取り出す
                Match afterCode = Regex.Match(rawMessage, @"error\s+CS\d{4}:\s*(.+)$");
                if (afterCode.Success)
                {
                    cleanMessage = afterCode.Groups[1].Value.Trim();
                }
            }

            return new CompilationError
            {
                Line = msg.line,
                Column = msg.column,
                Message = cleanMessage,
                ErrorCode = errorCode
            };
        }

        private static string[] GetAssemblyReferences()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic
                    && !string.IsNullOrEmpty(a.Location)
                    && File.Exists(a.Location))
                .Select(a => a.Location)
                .Distinct()
                .ToArray();
        }

        private static System.Reflection.Assembly LoadAssembly(string dllPath)
        {
            byte[] dllBytes = File.ReadAllBytes(dllPath);
            return System.Reflection.Assembly.Load(dllBytes);
        }

        /// <summary>
        /// ユーザーコードをExecuteAsyncメソッドのボディとしてラップする
        /// usingディレクティブは抽出してクラスの外に移動する
        /// </summary>
        private static string WrapCode(string code, string className, string namespaceName)
        {
            if (string.IsNullOrWhiteSpace(code)) code = "return null;";

            // デフォルトのusing群
            List<string> usings = new List<string>
            {
                "using System;",
                "using System.Collections.Generic;",
                "using System.Threading;",
                "using System.Threading.Tasks;",
                "using UnityEngine;",
                "using UnityEditor;"
            };

            List<string> bodyLines = new List<string>();
            foreach (string line in code.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("using ") && trimmed.EndsWith(";"))
                {
                    if (!usings.Contains(trimmed))
                    {
                        usings.Add(trimmed);
                    }
                }
                else
                {
                    bodyLines.Add(line.TrimEnd('\r'));
                }
            }

            string body = string.Join("\n", bodyLines);

            StringBuilder sb = new StringBuilder();
            foreach (string u in usings)
            {
                sb.AppendLine(u);
            }
            sb.AppendLine();
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
            sb.AppendLine($"    public class {className}");
            sb.AppendLine("    {");
            sb.AppendLine("        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct)");
            sb.AppendLine("        {");
            foreach (string line in body.Split('\n'))
            {
                sb.AppendLine("            " + line);
            }
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static void DeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
