using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Perilla.Mechanical.Core.Models;

namespace Perilla.Mechanical.Core.Services
{
    /// <summary>
    /// 训练样本存储服务：
    /// 使用本地 JSON 文件持久化存储训练样本。
    /// 支持添加、查询、导入、导出样本。
    /// </summary>
    public class TrainingSampleStore
    {
        private readonly string _storagePath;
        private readonly List<TrainingSample> _samples;

        public int Count { get { return _samples.Count; } }

        public TrainingSampleStore(string storagePath)
        {
            _storagePath = storagePath;
            _samples = new List<TrainingSample>();
            Load();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_storagePath)) return;
                string json = File.ReadAllText(_storagePath, Encoding.UTF8);
                var loaded = SimpleJsonSerializer.DeserializeList<TrainingSample>(json);
                if (loaded != null) _samples.AddRange(loaded);
            }
            catch
            {
                // 加载失败时忽略，从空样本库开始
            }
        }

        public void AddSample(TrainingSample sample)
        {
            if (sample == null) throw new ArgumentNullException("sample");
            _samples.Add(sample);
            Save();
        }

        public List<TrainingSample> GetAllSamples()
        {
            return new List<TrainingSample>(_samples);
        }

        public List<TrainingSample> GetSamplesByType(RecognitionKind kind)
        {
            var result = new List<TrainingSample>();
            foreach (var s in _samples)
            {
                if (s.CorrectType == kind) result.Add(s);
            }
            return result;
        }

        public int GetCorrectCount(RecognitionKind kind)
        {
            int n = 0;
            foreach (var s in _samples)
            {
                if (s.CorrectType == kind && s.IsCorrect) n++;
            }
            return n;
        }

        public int GetIncorrectCount(RecognitionKind kind)
        {
            int n = 0;
            foreach (var s in _samples)
            {
                if (s.RecognizedType == kind && !s.IsCorrect) n++;
            }
            return n;
        }

        public void Clear()
        {
            _samples.Clear();
            Save();
        }

        private void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(_storagePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string json = SimpleJsonSerializer.SerializeList(_samples);
                File.WriteAllText(_storagePath, json, Encoding.UTF8);
            }
            catch
            {
                // 保存失败时静默处理，不影响主程序运行
            }
        }

        public void ExportToFile(string filePath)
        {
            string json = SimpleJsonSerializer.SerializeList(_samples);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }

        public void ImportFromFile(string filePath)
        {
            string json = File.ReadAllText(filePath, Encoding.UTF8);
            var imported = SimpleJsonSerializer.DeserializeList<TrainingSample>(json);
            if (imported != null)
            {
                foreach (var s in imported)
                {
                    _samples.Add(s);
                }
                Save();
            }
        }
    }

    /// <summary>
    /// 简易 JSON 序列化器：避免引入第三方 JSON 库依赖。
    /// 仅支持 TrainingSample 和 RectD 的序列化/反序列化。
    /// </summary>
    internal static class SimpleJsonSerializer
    {
        public static string SerializeList<T>(List<T> list)
        {
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(SerializeSample(list[i] as TrainingSample));
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string SerializeSample(TrainingSample s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"SampleId\":\"").Append(Escape(s.SampleId)).Append("\",");
            sb.Append("\"PdfFileName\":\"").Append(Escape(s.PdfFileName)).Append("\",");
            sb.Append("\"PageIndex\":").Append(s.PageIndex).Append(",");
            sb.Append("\"ElementRegion\":").Append(SerializeRect(s.ElementRegion)).Append(",");
            sb.Append("\"CorrectType\":").Append((int)s.CorrectType).Append(",");
            sb.Append("\"RecognizedType\":").Append(s.RecognizedType.HasValue ? ((int)s.RecognizedType.Value).ToString() : "null").Append(",");
            sb.Append("\"RawText\":\"").Append(Escape(s.RawText)).Append("\",");
            sb.Append("\"IsCorrect\":").Append(s.IsCorrect ? "true" : "false").Append(",");
            sb.Append("\"CreatedAt\":\"").Append(s.CreatedAt.ToString("o")).Append("\",");
            sb.Append("\"Source\":\"").Append(Escape(s.Source)).Append("\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static string SerializeRect(RectD r)
        {
            return "{\"X\":" + r.X.ToString("0.###") +
                   ",\"Y\":" + r.Y.ToString("0.###") +
                   ",\"Width\":" + r.Width.ToString("0.###") +
                   ",\"Height\":" + r.Height.ToString("0.###") + "}";
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append('?');
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static List<TrainingSample> DeserializeList<T>(string json)
        {
            var result = new List<TrainingSample>();
            if (string.IsNullOrEmpty(json)) return result;

            int i = 0;
            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != '[') return result;
            i++;

            while (i < json.Length)
            {
                SkipWhitespace(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == ']') break;
                if (json[i] == ',') { i++; continue; }

                var sample = ParseSample(json, ref i);
                if (sample != null) result.Add(sample);
            }
            return result;
        }

        private static TrainingSample ParseSample(string json, ref int i)
        {
            var s = new TrainingSample();
            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != '{') return null;
            i++;

            while (i < json.Length)
            {
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] == '}') break;
                if (json[i] == ',') { i++; continue; }

                string key = ParseString(json, ref i);
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++;
                SkipWhitespace(json, ref i);

                switch (key)
                {
                    case "SampleId": s.SampleId = ParseString(json, ref i); break;
                    case "PdfFileName": s.PdfFileName = ParseString(json, ref i); break;
                    case "PageIndex": s.PageIndex = ParseInt(json, ref i); break;
                    case "ElementRegion": s.ElementRegion = ParseRect(json, ref i); break;
                    case "CorrectType": s.CorrectType = (RecognitionKind)ParseInt(json, ref i); break;
                    case "RecognizedType":
                        if (json[i] == 'n' && i + 3 < json.Length && json.Substring(i, 4) == "null")
                        { i += 4; s.RecognizedType = null; }
                        else s.RecognizedType = (RecognitionKind)ParseInt(json, ref i);
                        break;
                    case "RawText": s.RawText = ParseString(json, ref i); break;
                    case "IsCorrect": s.IsCorrect = ParseBool(json, ref i); break;
                    case "CreatedAt":
                        string dtStr = ParseString(json, ref i);
                        try { s.CreatedAt = DateTime.Parse(dtStr); } catch { }
                        break;
                    case "Source": s.Source = ParseString(json, ref i); break;
                    default: SkipValue(json, ref i); break;
                }
            }
            if (i < json.Length && json[i] == '}') i++;
            return s;
        }

        private static RectD ParseRect(string json, ref int i)
        {
            double x = 0, y = 0, w = 0, h = 0;
            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != '{') return new RectD(0, 0, 0, 0);
            i++;

            while (i < json.Length)
            {
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] == '}') break;
                if (json[i] == ',') { i++; continue; }

                string key = ParseString(json, ref i);
                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++;
                SkipWhitespace(json, ref i);

                switch (key)
                {
                    case "X": x = ParseDouble(json, ref i); break;
                    case "Y": y = ParseDouble(json, ref i); break;
                    case "Width": w = ParseDouble(json, ref i); break;
                    case "Height": h = ParseDouble(json, ref i); break;
                    default: SkipValue(json, ref i); break;
                }
            }
            if (i < json.Length && json[i] == '}') i++;
            return new RectD(x, y, w, h);
        }

        private static string ParseString(string json, ref int i)
        {
            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != '"') return "";
            i++;
            var sb = new StringBuilder();
            while (i < json.Length && json[i] != '"')
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    i++;
                    switch (json[i])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(json[i]); break;
                    }
                }
                else
                {
                    sb.Append(json[i]);
                }
                i++;
            }
            if (i < json.Length && json[i] == '"') i++;
            return sb.ToString();
        }

        private static int ParseInt(string json, ref int i)
        {
            SkipWhitespace(json, ref i);
            int start = i;
            if (i < json.Length && (json[i] == '-' || json[i] == '+')) i++;
            while (i < json.Length && char.IsDigit(json[i])) i++;
            int result;
            int.TryParse(json.Substring(start, i - start), out result);
            return result;
        }

        private static double ParseDouble(string json, ref int i)
        {
            SkipWhitespace(json, ref i);
            int start = i;
            if (i < json.Length && (json[i] == '-' || json[i] == '+')) i++;
            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '.')) i++;
            double result;
            double.TryParse(json.Substring(start, i - start),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result);
            return result;
        }

        private static bool ParseBool(string json, ref int i)
        {
            SkipWhitespace(json, ref i);
            if (i + 3 < json.Length && json.Substring(i, 4) == "true") { i += 4; return true; }
            if (i + 4 < json.Length && json.Substring(i, 5) == "false") { i += 5; return false; }
            return false;
        }

        private static void SkipValue(string json, ref int i)
        {
            SkipWhitespace(json, ref i);
            if (i >= json.Length) return;
            if (json[i] == '"') { ParseString(json, ref i); }
            else if (json[i] == '{')
            {
                int depth = 1; i++;
                while (i < json.Length && depth > 0)
                {
                    if (json[i] == '{') depth++;
                    else if (json[i] == '}') depth--;
                    else if (json[i] == '"') { ParseString(json, ref i); continue; }
                    i++;
                }
            }
            else if (json[i] == '[')
            {
                int depth = 1; i++;
                while (i < json.Length && depth > 0)
                {
                    if (json[i] == '[') depth++;
                    else if (json[i] == ']') depth--;
                    else if (json[i] == '"') { ParseString(json, ref i); continue; }
                    i++;
                }
            }
            else
            {
                while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']') i++;
            }
        }

        private static void SkipWhitespace(string json, ref int i)
        {
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        }
    }
}
