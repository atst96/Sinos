using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using MusicXml;
using MusicXml.NoteElements;
using Quark.Models;
using Quark.Models.Scores;

namespace Quark.Utils;

/// <summary>
/// MusicXMLファイルの処理に関するUtil
/// </summary>
public static class MusicXmlUtil
{
    /// <summary>テンポ未指定時に使用するテンポ(BPM)</summary>
    const double DefaultTempo = 100;

    /// <summary>
    /// MusicXMLからパート情報を列挙する。
    /// </summary>
    /// <param name="xmlStream"></param>
    /// <returns></returns>
    public static IEnumerable<(ScorePartElement Info, Part Part)> EnumerateParts(Stream xmlStream)
    {
        var score = MusicXmlDeserializer.Parse(xmlStream);
        if (score == null || score.Parts is not { Count: > 0 } scoreParts)
            return []; // パート情報がなければ空で返す

        // パートIDをキーにしてDictionary化
        var partInfoByPartId = score.PartList?.ScorePart
            ?.Where(i => i != null && i.Id != null)
            ?.ToDictionary(i => i.Id!)
            ?? [];

        return [.. scoreParts.Where(i => i != null)
            .Select(scorePart =>
            {
                ScorePartElement? info;

                var partId = scorePart.Id;
                if (partId == null || !partInfoByPartId.TryGetValue(partId, out info))
                {
                    info = new();
                }

                return (info, scorePart);
            })];
    }

    [Obsolete("This class method will delete.", false)]
    public static ScoreInfo Parse(string xml)
    {
        byte[] data = new UTF8Encoding(false).GetBytes(xml);

        using var ms = new MemoryStream(data);
        var part = EnumerateParts(ms).First();

        return TryParse(part.Part, out var score)
            ? score
            : throw new NotSupportedException(); // TODO
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static decimal TempoToTick(decimal tempo) => 60 / tempo;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static decimal GetQuarterDuration(decimal unit, decimal tempo)
            => unit * TempoToTick(tempo) * 1000;

    /// <summary>
    /// MusicXMLのパート毎の演奏情報を解析する。
    /// </summary>
    /// <param name="part">パート情報</param>
    /// <param name="score">解析後情報</param>
    /// <returns></returns>
    private static bool TryParse(Part part, [NotNullWhen(true)] out ScoreInfo? score)
    {
        var measures = part.Measures ?? [];

        var scoreNotes = new LinkedList<ScoreNote>();
        var tempos = new LinkedList<TempoInfo>();
        var timeSignatures = new LinkedList<TimeSignature>();

        // second
        decimal currentTime = 0;
        ScoreNote? tiedNote = null;

        double tempo = DefaultTempo;
        // 4分音符の分割数
        float division = 1;
        decimal unit = 1;

        // 4部音符あたりの時間
        decimal timePerQuarter = GetQuarterDuration(unit, (decimal)tempo);
        Dictionary<int, int>? keySignature = null;
        int noteIdx = -1;

        // 小節分解析を繰り返す
        for (int measureIdx = 0; measureIdx < measures.Count; ++measureIdx)
        {
            var measure = measures[measureIdx];

            // 小節内の音符の開始位置。divisionsの値を用いる
            int noteOffset = 0;

            if (measure.Attributes is { } attributes)
            {
                if (attributes.Divisions is { } newDivision)
                {
                    division = newDivision;
                    unit = 1 / (decimal)newDivision;
                    timePerQuarter = GetQuarterDuration(unit, (decimal)tempo);
                }

                if (attributes.Time is { } time)
                    timeSignatures.AddLast(new TimeSignature(measureIdx, noteOffset, currentTime, time.Beats, time.BeatType));

                var fifth = attributes.Key?.Fifths;
                if (fifth is null || fifth == 0)
                {
                    // 調号なし
                    keySignature = null;
                }
                else
                {
                    if (KeySignatures.TryGetValue(fifth.Value, out var nextKeySignature))
                    {
                        keySignature = nextKeySignature;
                    }
                    else
                    {
                        // 想定外の調号だった場合
                        Debug.WriteLine(attributes.Key!.Fifths);
                        Debugger.Break();
                    }
                }
            }

            foreach (var item in measure.Items ?? [])
            {
                if (item is Direction direction)
                {
                    if (direction.Sound is { } sound && direction.DirectionType?.Metronome is { } metronome)
                    {
                        tempo = sound.Tempo;
                        timePerQuarter = GetQuarterDuration(unit, (decimal)tempo);

                        tempos.AddLast(new TempoInfo(measureIdx, noteOffset, currentTime, tempo, metronome.BeatUnit ?? "4", metronome.BeatUnitDot != null, metronome.PerMinute));
                    }
                }
                else if (item is Note note)
                {
                    var duration = note.Duration * timePerQuarter;
                    var pitch = note.Pitch;

                    try
                    {
                        if (note.Rest is not null)
                        {
                            // 休符
                            continue;
                        }
                        else if (pitch is not null)
                        {
                            var ties = note.Tie;
                            if (ties is null || ties.Count == 0)
                            {
                                // タイ以外の音符
                                scoreNotes.AddLast(CreateFrameInfo(++noteIdx, measureIdx, noteOffset, note, currentTime, currentTime + duration));
                            }
                            else
                            {
                                if (ties.All(t => t.Type == StartStop.Start))
                                {
                                    // タイ記号の始め

                                    // 音符リストには追加せずに、タイの終わりまで蓄積する
                                    tiedNote = CreateFrameInfo(++noteIdx, measureIdx, noteOffset, note, currentTime, currentTime + duration);
                                }
                                else if (ties.Any(t => t.Type == StartStop.Stop) && tiedNote is not null)
                                {
                                    // タイの終わり

                                    tiedNote.Notes.Add(note);

                                    if (ties.Any(t => t.Type == StartStop.Start))
                                    {
                                        // 3つ以上の音符がタイ記号で連なっている場合の、最初と最後以外の音符
                                        // pass
                                    }
                                    else
                                    {
                                        // タイ記号の終わり位置

                                        // タイの開始から終了までの情報を追加
                                        tiedNote.SetEndFrame((int)(currentTime + duration));
                                        tiedNote.SetBreath(GetIsBreath(note));

                                        scoreNotes.AddLast(tiedNote);

                                        tiedNote = null;
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine(note);
                                    Debugger.Break();
                                }
                            }
                        }
                        else
                        {
                            // 音符じゃない場合？
                            Debug.WriteLine(note);
                            Debugger.Break();
                        }
                    }
                    finally
                    {
                        currentTime += duration;
                        noteOffset += note.Duration;
                    }
                }
            }
        }

        // 先頭のテンポ情報がなければデフォルトを差し込む
        if (tempos is not { Count: > 0, First.Value.Time: 0 })
        {
            tempos.AddFirst(new TempoInfo(0, 0, 0, DefaultTempo, "quarter", false, DefaultTempo));
        }

        // 先頭の小節情報がなければデフォルトを差し込む
        if (timeSignatures is not { Count: > 0, First.Value.Time: 0 })
        {
            timeSignatures.AddFirst(new TimeSignature(0, 0, 0, 4, 4));
        }

        score = new ScoreInfo(0, tempos, timeSignatures, scoreNotes, new(measures));
        return true;
    }

    /// <summary>
    /// パートごとのMusicXMLを作成する。
    /// </summary>
    /// <param name="part">パート情報</param>
    /// <param name="partName">パート名</param>
    /// <returns>XML文字列</returns>
    public static string ToXmlString(Part part, string partName)
        => MusicXmlSerializer.ToXmlString(new MusicXmlObject()
        {
            Version = "4.0",
            Identification = new()
            {
                Encoding = new() { Software = "Quark", EncodingDate = DateTime.Now }
            },
            PartList = new()
            {
                ScorePart = [new() { Id = "1", PartName = partName }]
            },
            Parts = [new() { Id = "1", Measures = part.Measures }]
        });

    static int GetCode(Pitch pitch)
    {
        int timble = KeyCodeForStep[pitch.Step];

        return (int)((pitch.Octave * 12) + (pitch.Alter ?? 0) + timble + 13);
    }

    private static bool GetIsBreath(Note note)
        => note.Notations?.Articulations?.BreathMark is not null;

    private static ScoreNote CreateFrameInfo(int noteIdx, int measureIdx, int offset, Note note, decimal startTime, decimal endTime)
        => new()
        {
            Index = noteIdx,
            MeasureIdx = measureIdx,
            Offset = offset,
            BeginTime = (int)startTime,
            EndTime = (int)endTime,
            Lyrics = note.Lyric?.Text ?? string.Empty,
            Pitch = GetCode(note.Pitch),
            IsBreath = GetIsBreath(note),
            Notes = [note],
        };

    private const int KeyCodeC = 0;
    private const int KeyCodeCSharp = 1;
    private const int KeyCodeD = 2;
    private const int KeyCodeDSharp = 3;
    private const int KeyCodeE = 4;
    private const int KeyCodeF = 5;
    private const int KeyCodeFSharp = 6;
    private const int KeyCodeG = 7;
    private const int KeyCodeGSharp = 8;
    private const int KeyCodeA = 9;
    private const int KeyCodeASharp = 10;
    private const int KeyCodeB = 11;

    private static readonly ImmutableDictionary<string, int> KeyCodeForStep = new Dictionary<string, int>()
    {
        ["C"] = KeyCodeC,
        ["D"] = KeyCodeD,
        ["E"] = KeyCodeE,
        ["F"] = KeyCodeF,
        ["G"] = KeyCodeG,
        ["A"] = KeyCodeA,
        ["B"] = KeyCodeB,
    }
    .ToImmutableDictionary();

    private static readonly ImmutableDictionary<int, Dictionary<int, int>> KeySignatures = new Dictionary<int, Dictionary<int, int>>()
    {
        [-7] = new() // ♭7つ
        {
            [KeyCodeF] = KeyCodeF - 1,
            [KeyCodeE] = KeyCodeE - 1,
            [KeyCodeD] = KeyCodeD - 1,
            [KeyCodeC] = KeyCodeC - 1,
            [KeyCodeB] = KeyCodeB - 1,
            [KeyCodeA] = KeyCodeA - 1,
            [KeyCodeG] = KeyCodeG - 1,
        },
        [-6] = new() // ♭6つ
        {
            [KeyCodeE] = KeyCodeE - 1,
            [KeyCodeD] = KeyCodeD - 1,
            [KeyCodeC] = KeyCodeC - 1,
            [KeyCodeB] = KeyCodeB - 1,
            [KeyCodeA] = KeyCodeA - 1,
            [KeyCodeG] = KeyCodeG - 1,
        },
        [-5] = new() // ♭5つ
        {
            [KeyCodeE] = KeyCodeE - 1,
            [KeyCodeD] = KeyCodeD - 1,
            [KeyCodeB] = KeyCodeB - 1,
            [KeyCodeA] = KeyCodeA - 1,
            [KeyCodeG] = KeyCodeG - 1,
        },
        [-4] = new() // ♭4つ
        {
            [KeyCodeE] = KeyCodeE - 1,
            [KeyCodeD] = KeyCodeD - 1,
            [KeyCodeB] = KeyCodeB - 1,
            [KeyCodeA] = KeyCodeA - 1,
        },
        [-3] = new() // ♭3つ
        {
            [KeyCodeE] = KeyCodeE - 1,
            [KeyCodeB] = KeyCodeB - 1,
            [KeyCodeA] = KeyCodeA - 1,
        },
        [-2] = new() // ♭2つ
        {
            [KeyCodeE] = KeyCodeE - 1,
            [KeyCodeB] = KeyCodeB - 1,
        },
        [-1] = new() // ♭1つ
        {
            [KeyCodeB] = KeyCodeB - 1,
        },
        [1] = new() // ♯1つ
        {
            [KeyCodeF] = KeyCodeF + 1,
        },
        [2] = new() // ♯2つ
        {
            [KeyCodeF] = KeyCodeF + 1,
            [KeyCodeC] = KeyCodeC + 1,
        },
        [3] = new()// ♯3つ
        {
            [KeyCodeG] = KeyCodeG + 1,
            [KeyCodeF] = KeyCodeF + 1,
            [KeyCodeC] = KeyCodeC + 1,
        },
        [4] = new()// ♯4つ
        {
            [KeyCodeG] = KeyCodeG + 1,
            [KeyCodeF] = KeyCodeF + 1,
            [KeyCodeD] = KeyCodeD + 1,
            [KeyCodeC] = KeyCodeC + 1,
        },
        [5] = new()// ♯5つ
        {
            [KeyCodeG] = KeyCodeG + 1,
            [KeyCodeF] = KeyCodeF + 1,
            [KeyCodeD] = KeyCodeD + 1,
            [KeyCodeC] = KeyCodeC + 1,
            [KeyCodeA] = KeyCodeA + 1,
        },
        [6] = new()// ♯6つ
        {
            [KeyCodeG] = KeyCodeG + 1,
            [KeyCodeF] = KeyCodeF + 1,
            [KeyCodeD] = KeyCodeD + 1,
            [KeyCodeC] = KeyCodeC + 1,
            [KeyCodeB] = KeyCodeB + 1,
            [KeyCodeA] = KeyCodeA + 1,
        },
    }
    .ToImmutableDictionary();
}
