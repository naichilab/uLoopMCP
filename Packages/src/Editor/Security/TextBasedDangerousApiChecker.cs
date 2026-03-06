using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// テキストベースの危険API検出器
    /// Roslynが利用できない環境でRestricted モードのセキュリティチェックを行う
    /// </summary>
    internal static class TextBasedDangerousApiChecker
    {
        private static readonly (string Pattern, string ApiName)[] DangerousPatterns =
        {
            // ファイルI/O
            (@"\bSystem\.IO\b",                        "System.IO"),
            (@"\bFile\.",                               "File"),
            (@"\bDirectory\.",                          "Directory"),
            (@"\bPath\.",                               "Path"),
            (@"\bFileStream\b",                        "FileStream"),
            (@"\bStreamWriter\b",                      "StreamWriter"),
            (@"\bStreamReader\b",                      "StreamReader"),
            (@"\bBinaryWriter\b",                      "BinaryWriter"),
            (@"\bBinaryReader\b",                      "BinaryReader"),
            (@"\bFileUtil\.",                           "FileUtil"),
            // プロセス
            (@"\bProcess\b",                           "Process"),
            // ネットワーク
            (@"\bSocket\b",                            "Socket"),
            (@"\bTcpClient\b",                         "TcpClient"),
            (@"\bUdpClient\b",                         "UdpClient"),
            (@"\bNetworkStream\b",                     "NetworkStream"),
            (@"\bWebClient\b",                         "WebClient"),
            (@"\bHttpClient\b",                        "HttpClient"),
            (@"\bHttpWebRequest\b",                    "HttpWebRequest"),
            (@"\bWebRequest\b",                        "WebRequest"),
            (@"\bUnityWebRequest\b",                   "UnityWebRequest"),
            // リフレクション経由のアセンブリロード
            (@"\bAssembly\.Load\b",                    "Assembly.Load"),
            (@"\bAssembly\.LoadFrom\b",                "Assembly.LoadFrom"),
            (@"\bAssembly\.LoadFile\b",                "Assembly.LoadFile"),
            // 環境・システム
            (@"\bEnvironment\.Exit\b",                 "Environment.Exit"),
            // セキュリティレベルの自己昇格防止
            (@"\bULoopSettings\b",                     "ULoopSettings"),
            (@"\bDynamicCodeSecurityManager\b",        "DynamicCodeSecurityManager"),
            // アセット破壊操作
            (@"\bAssetDatabase\.CreateFolder\b",       "AssetDatabase.CreateFolder"),
            (@"\bAssetDatabase\.DeleteAsset\b",        "AssetDatabase.DeleteAsset"),
            (@"\bAssetDatabase\.MoveAsset\b",          "AssetDatabase.MoveAsset"),
            (@"\bAssetDatabase\.CopyAsset\b",          "AssetDatabase.CopyAsset"),
        };

        /// <summary>
        /// コードを検査し、セキュリティ違反のリストを返す
        /// </summary>
        public static List<SecurityViolation> Check(string code)
        {
            List<SecurityViolation> violations = new List<SecurityViolation>();
            if (string.IsNullOrWhiteSpace(code)) return violations;

            string[] lines = code.Split('\n');
            foreach ((string pattern, string apiName) in DangerousPatterns)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (Regex.IsMatch(lines[i], pattern))
                    {
                        violations.Add(new SecurityViolation
                        {
                            Type = SecurityViolationType.DangerousApiCall,
                            ApiName = apiName,
                            Description = $"Dangerous API detected: {apiName}",
                            Message = $"Use of '{apiName}' is not allowed in Restricted mode.",
                            LineNumber = i + 1,
                            CodeSnippet = lines[i].Trim()
                        });
                        break;
                    }
                }
            }
            return violations;
        }
    }
}
