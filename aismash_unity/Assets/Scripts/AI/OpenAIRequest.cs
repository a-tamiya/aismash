using System.Text;

namespace PromptFighters.AI
{
    // OpenAI APIへ送るリクエストボディを安全に組み立てるヘルパ。
    // 手書きの .Replace チェーンでは制御文字(U+0000〜U+001F)を取りこぼし
    // 不正JSON→400になるため、全制御文字を \uXXXX でエスケープする。
    public static class OpenAIRequest
    {
        // JSON文字列リテラルの中身としてsを安全にエスケープする（RFC 8259準拠）。
        public static string EscapeString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // chat/completions のボディを組む。
        // systemPromptを与えると信頼できる指示(system)とユーザー入力(user)を分離できる。
        // jsonMode=true で response_format を json_object に固定する。
        public static string BuildChatBody(string model, string systemPrompt, string userPrompt,
                                           bool jsonMode = false)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"model\":\"").Append(EscapeString(model)).Append('"');
            if (jsonMode)
                sb.Append(",\"response_format\":{\"type\":\"json_object\"}");
            sb.Append(",\"messages\":[");
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                sb.Append("{\"role\":\"system\",\"content\":\"")
                  .Append(EscapeString(systemPrompt)).Append("\"},");
            }
            sb.Append("{\"role\":\"user\",\"content\":\"")
              .Append(EscapeString(userPrompt)).Append("\"}]}");
            return sb.ToString();
        }
    }
}
