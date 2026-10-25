using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Dispatching;
using Wordsbook.Models;

namespace Wordsbook.Services;

/// <summary>
/// JSON 单文件存储。写入走临时文件 + File.Replace 保证原子性，
/// 并通过 400ms 防抖合并连续的编辑操作，避免每次按键都落盘。
/// </summary>
public sealed class Store
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string StoreDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wordsbook");

    public static string FilePath { get; } = Path.Combine(StoreDir, "wordsbook.json");

    readonly DispatcherQueueTimer _timer;
    readonly object _gate = new();

    public AppData Data { get; private set; } = new();

    public Store(DispatcherQueue queue)
    {
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(400);
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Flush();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                var parsed = JsonSerializer.Deserialize<AppData>(text, JsonOpts);
                if (parsed is not null) Data = parsed;
            }
        }
        catch
        {
            // 损坏时保留备份并从头开始，绝不静默覆盖原文件。
            try { File.Copy(FilePath, FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), overwrite: true); } catch { }
            Data = new AppData();
        }

        if (Data.Languages.Count == 0) SeedData();
    }

    void SeedData()
    {
        var nl = new Language { Name = "Nederlands", Code = "nl" };
        var es = new Language { Name = "Español", Code = "es" };
        Data.Languages.AddRange([nl, es]);
        Data.Settings.ActiveLangId = nl.Id;
    }

    public void RequestSave()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Flush()
    {
        _timer.Stop();
        string json;
        lock (_gate)
        {
            json = JsonSerializer.Serialize(Data, JsonOpts);
        }

        try
        {
            Directory.CreateDirectory(StoreDir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
        catch
        {
            // 落盘失败不应打断编辑；下一次 RequestSave 会重试。
        }
    }
}
