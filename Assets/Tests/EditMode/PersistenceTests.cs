using System.Globalization;
using System.Threading;
using NUnit.Framework;
using QuestSaturation.Core;

namespace QuestSaturation.Tests
{
    public class SettingsCodecTests
    {
        [Test]
        public void RoundTrips()
        {
            var s = FreeControlSettings.CreateDefault();
            s.Brightness = -0.25f;
            s.Contrast = 0.4f;
            s.Saturation = -0.85f;
            s.Waveform = Waveform.Ramp;
            s.From = 0.1f;
            s.To = 0.9f;
            s.PeriodSec = 42.5f;
            s.Steps = 7;
            s.ParticipantId = 123;
            var back = SettingsCodec.Decode(SettingsCodec.Encode(s), out var warning);
            Assert.That(warning, Is.Null);
            Assert.That(back.Brightness, Is.EqualTo(s.Brightness));
            Assert.That(back.Contrast, Is.EqualTo(s.Contrast));
            Assert.That(back.Saturation, Is.EqualTo(s.Saturation));
            Assert.That(back.Waveform, Is.EqualTo(s.Waveform));
            Assert.That(back.From, Is.EqualTo(s.From));
            Assert.That(back.To, Is.EqualTo(s.To));
            Assert.That(back.PeriodSec, Is.EqualTo(s.PeriodSec));
            Assert.That(back.Steps, Is.EqualTo(s.Steps));
            Assert.That(back.ParticipantId, Is.EqualTo(s.ParticipantId));
        }

        [Test]
        public void EncodesWithVersionAndInvariantNumbers()
        {
            var prev = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var s = FreeControlSettings.CreateDefault();
                s.Saturation = -0.5f;
                string json = SettingsCodec.Encode(s);
                Assert.That(json, Does.StartWith("{\"version\":1,"));
                Assert.That(json, Does.Contain("\"saturation\":-0.5"));
                Assert.That(SettingsCodec.Decode(json, out _).Saturation, Is.EqualTo(-0.5f));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = prev;
            }
        }

        [Test]
        public void ClampsOutOfRangeValues()
        {
            const string json = "{\"version\":1,\"brightness\":5,\"contrast\":-9,\"saturation\":1e30,\"periodSec\":0.01," +
                                "\"steps\":100,\"participantId\":-4,\"waveform\":\"Nope\",\"from\":\"x\"}";
            var s = SettingsCodec.Decode(json, out var warning);
            Assert.That(warning, Is.Null);
            Assert.That(s.Brightness, Is.EqualTo(0.5f));
            Assert.That(s.Contrast, Is.EqualTo(-1f));
            Assert.That(s.Saturation, Is.EqualTo(1f));
            Assert.That(s.PeriodSec, Is.EqualTo(1f));
            Assert.That(s.Steps, Is.EqualTo(20));
            Assert.That(s.ParticipantId, Is.EqualTo(0));
            Assert.That(s.Waveform, Is.EqualTo(Waveform.Sine), "unknown name → default");
            Assert.That(s.From, Is.EqualTo(0f), "unparsable → default");
        }

        [Test]
        public void NumericWaveformIsRejected()
        {
            var s = SettingsCodec.Decode("{\"version\":1,\"waveform\":\"3\"}", out _);
            Assert.That(s.Waveform, Is.EqualTo(Waveform.Sine));
            s = SettingsCodec.Decode("{\"version\":1,\"waveform\":3}", out _);
            Assert.That(s.Waveform, Is.EqualTo(Waveform.Sine));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json")]
        [TestCase("{\"version\":1,")]
        [TestCase("{\"a\":{\"b\":1}}")]
        [TestCase("[1,2]")]
        public void BrokenInputGivesDefaultsAndWarning(string json)
        {
            var s = SettingsCodec.Decode(json, out var warning);
            Assert.That(warning, Is.Not.Null);
            Assert.That(s.Saturation, Is.EqualTo(0f));
            Assert.That(s.PeriodSec, Is.EqualTo(10f));
        }

        [Test]
        public void ReportsMissingAndNewerVersion()
        {
            SettingsCodec.Decode("{\"saturation\":0.5}", out var w1);
            Assert.That(w1, Is.EqualTo("missing_version"));
            var s = SettingsCodec.Decode("{\"version\":9,\"saturation\":0.5}", out var w2);
            Assert.That(w2, Is.EqualTo("newer_version:9"));
            Assert.That(s.Saturation, Is.EqualTo(0.5f), "known fields are still read");
        }

        [Test]
        public void DoesNotStoreModeOrPlayState()
        {
            string json = SettingsCodec.Encode(FreeControlSettings.CreateDefault());
            Assert.That(json, Does.Not.Contain("mode"));
            Assert.That(json, Does.Not.Contain("playing"));
        }

        [Test]
        public void DefaultsMatchParameterDefs()
        {
            var s = FreeControlSettings.CreateDefault();
            foreach (var d in ParameterDefs.All)
            {
                if (d.Item == MenuItem.Mode) continue;
                Assert.That(s.Get(d.Item), Is.EqualTo(d.Default), d.Item.ToString());
            }
        }
    }

    public class CsvTests
    {
        [Test]
        public void ColumnEnumMatchesHeader()
        {
            Assert.That(System.Enum.GetValues(typeof(CsvColumn)).Length, Is.EqualTo(CsvFormat.ColumnCount));
            Assert.That(CsvFormat.Columns[(int)CsvColumn.SessionId], Is.EqualTo("session_id"));
            Assert.That(CsvFormat.Columns[(int)CsvColumn.TrialElapsedSec], Is.EqualTo("trial_elapsed_s"));
            Assert.That(CsvFormat.Columns[(int)CsvColumn.Detail], Is.EqualTo("detail"));
            Assert.That(CsvFormat.Header.Split(',').Length, Is.EqualTo(CsvFormat.ColumnCount));
        }

        [Test]
        public void RowAlwaysHasHeaderColumnCount()
        {
            var row = new CsvRow();
            Assert.That(CountFields(row.ToLine()), Is.EqualTo(CsvFormat.ColumnCount));
            row.Set(CsvColumn.Event, "marker").Set(CsvColumn.Detail, "a,b \"c\"\nd");
            Assert.That(CountFields(row.ToLine()), Is.EqualTo(CsvFormat.ColumnCount));
            row.Clear();
            Assert.That(row.Get(CsvColumn.Event), Is.EqualTo(""));
        }

        [Test]
        public void EscapesQuotesCommasAndNewlines()
        {
            Assert.That(CsvFormat.Escape("plain"), Is.EqualTo("plain"));
            Assert.That(CsvFormat.Escape("a,b"), Is.EqualTo("\"a,b\""));
            Assert.That(CsvFormat.Escape("say \"hi\""), Is.EqualTo("\"say \"\"hi\"\"\""));
            Assert.That(CsvFormat.Escape("x\ny"), Is.EqualTo("\"x\ny\""));
            Assert.That(CsvFormat.Escape(null), Is.EqualTo(""));
        }

        [Test]
        public void NumbersUseInvariantCultureEvenInGermanLocale()
        {
            var prev = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var row = new CsvRow();
                row.Set(CsvColumn.Saturation, -0.8f).Set(CsvColumn.TRealtimeSec, 1234.5678).Set(CsvColumn.Frame, 1234567L);
                string line = row.ToLine();
                Assert.That(line, Does.Contain("-0.8"));
                Assert.That(line, Does.Contain("1234.5678"));
                Assert.That(line, Does.Contain("1234567"));
                Assert.That(line, Does.Not.Contain("-0,8"));
                Assert.That(CountFields(line), Is.EqualTo(CsvFormat.ColumnCount));
                Assert.That(CsvFormat.Utc(new System.DateTime(2026, 1, 2, 3, 4, 5, 678, System.DateTimeKind.Utc)),
                    Is.EqualTo("2026-01-02T03:04:05.678Z"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = prev;
            }
        }

        [Test]
        public void NonFiniteNumbersBecomeEmpty()
        {
            Assert.That(CsvFormat.Float(float.NaN), Is.EqualTo(""));
            Assert.That(CsvFormat.Double(double.PositiveInfinity), Is.EqualTo(""));
            Assert.That(CsvFormat.Bool(true), Is.EqualTo("1"));
        }

        // RFC 4180 に沿って、引用符の中のカンマを数えずにフィールド数を数える
        private static int CountFields(string line)
        {
            int n = 1;
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') quoted = !quoted;
                else if (c == ',' && !quoted) n++;
            }
            return n;
        }
    }
}
