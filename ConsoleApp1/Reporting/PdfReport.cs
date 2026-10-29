using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DeviceScanner.Scanning;
using DeviceScanner.Utils;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;

namespace DeviceScanner.Reporting
{
    /// <summary>
    /// The scan report as a PDF, laid out like docs/ScanReport_Figma_Template.svg: header band with the overall verdict,
    /// a module results table, then one section per module with its own PASS / WARN / FAIL / NOT RUN badge.
    /// Encrypted (printing only) so the report cannot be edited after the run.
    /// </summary>
    internal sealed class PdfReport
    {
        private enum Tone { Pass, Warn, Fail, Neutral }

        // palette of the Figma template
        private static readonly Color Navy = Hex(0x1F3A5F);
        private static readonly Color NavyLight = Hex(0xC9D6E6);
        private static readonly Color Ink = Hex(0x1F2933);
        private static readonly Color Muted = Hex(0x6B7785);
        private static readonly Color Rule = Hex(0xD5DBE1);
        private static readonly Color HeadBg = Hex(0xEEF2F6);
        private static readonly Color Zebra = Hex(0xFAFBFC);

        private const float PageMargin = 36;

        private readonly ScanConfig _cfg;
        private PdfFont _regular;
        private PdfFont _bold;

        public PdfReport(ScanConfig cfg) { _cfg = cfg; }

        public void Save(string path, RunReport r, IList<ModuleResult> modules, DateTime finishedAt)
        {
            var props = new WriterProperties()
                .SetStandardEncryption(
                    null,
                    Encoding.UTF8.GetBytes(_cfg.Password),
                    EncryptionConstants.ALLOW_PRINTING,
                    EncryptionConstants.ENCRYPTION_AES_256);

            using (var writer = new PdfWriter(path, props))
            using (var pdf = new PdfDocument(writer))
            using (var doc = new Document(pdf, iText.Kernel.Geom.PageSize.A4, false))    // not flushed: footers are added at the end
            {
                LoadFonts();
                doc.SetMargins(PageMargin, PageMargin, PageMargin + 14, PageMargin);
                doc.SetFont(_regular).SetFontSize(8).SetFontColor(Ink);

                var verdict = ModuleVerdicts.Overall(modules);
                Header(doc, r, verdict, finishedAt);
                ModuleTable(doc, modules);

                foreach (var m in modules)
                {
                    Section(doc, m);
                    if (m.Status == ModuleStatus.NotRun) continue;
                    switch (m.Module)
                    {
                        case Module.WindowsSpec: SpecTable(doc, r); break;
                        case Module.Devices: DeviceTable(doc, r); break;
                        case Module.Homing: HomingTable(doc, r); break;
                        case Module.Io: IoTable(doc, r); break;
                        case Module.Discovery: DiscoveryTable(doc, r); break;
                    }
                }

                Result(doc, verdict, path);
                Footers(doc, pdf, Path.GetFileName(path));
            }
        }

        // ---------- page parts ----------

        private void Header(Document doc, RunReport r, RunVerdict verdict, DateTime finishedAt)
        {
            var band = new Table(UnitValue.CreatePercentArray(new float[] { 68, 32 })).UseAllAvailableWidth();
            band.AddCell(Plain().SetBackgroundColor(Navy).SetPadding(14)
                .Add(new Paragraph(L.T("pdf.title")).SetFont(_bold).SetFontSize(18).SetFontColor(ColorConstants.WHITE))
                .Add(new Paragraph(L.T("pdf.profile", r.Profile.Name)).SetFontSize(10).SetFontColor(NavyLight)));
            band.AddCell(Plain().SetBackgroundColor(Navy).SetPadding(14)
                .SetTextAlignment(TextAlignment.RIGHT).SetVerticalAlignment(VerticalAlignment.MIDDLE)
                .Add(new Paragraph(L.T("pdf.overall")).SetFontSize(7.5f).SetFontColor(NavyLight).SetMarginBottom(4))
                .Add(new Paragraph().Add(Badge(verdict.Passed ? Tone.Pass : Tone.Fail, L.T(verdict.Passed ? "tag.pass" : "tag.fail"), 14))));
            doc.Add(band);

            var info = new Table(UnitValue.CreatePercentArray(new float[] { 22, 22, 22, 34 })).UseAllAvailableWidth().SetMarginTop(10);
            info.AddCell(Field(L.T("pdf.user"), r.UserName));
            info.AddCell(Field(L.T("pdf.started"), r.StartedAt.ToString("yyyy-MM-dd HH:mm:ss")));
            info.AddCell(Field(L.T("pdf.finished"), finishedAt.ToString("yyyy-MM-dd HH:mm:ss")));
            info.AddCell(Field(L.T("pdf.profileFile"), r.Profile.SourcePath ?? ""));
            doc.Add(info);

            doc.Add(Counts(verdict).SetMarginTop(8));
        }

        private void ModuleTable(Document doc, IList<ModuleResult> modules)
        {
            doc.Add(Heading(L.T("pdf.modules"), null));
            var t = DataTable(new float[] { 13, 22, 53, 12 },
                L.T("col.status"), L.T("col.module"), L.T("col.result"), L.T("col.duration"));
            int row = 0;
            foreach (var m in modules)
                Row(t, row++, ToneOf(m.Status), TagOf(m.Status), m.Title, m.Summary, m.Duration.HasValue ? Elapsed(m.Duration.Value) : "—");
            doc.Add(t);
        }

        private void Section(Document doc, ModuleResult m)
        {
            doc.Add(Heading(m.Title, m).SetMarginTop(16));
            doc.Add(new Paragraph(m.Summary).SetFontColor(Muted).SetMarginTop(0).SetMarginBottom(2).SetKeepWithNext(true));
        }

        private void SpecTable(Document doc, RunReport r)
        {
            var t = DataTable(new float[] { 10, 14, 20, 18, 16, 22 },
                L.T("col.status"), L.T("col.group"), L.T("col.check"), L.T("col.actual"), L.T("col.expected"), L.T("col.detail"));
            int row = 0;
            foreach (var s in r.WindowsSpec.Items)
            {
                string detail = string.Join("\n", new[] { s.Detail }.Concat(s.Notes).Where(x => !string.IsNullOrEmpty(x)));
                Row(t, row++, ToneOf(s.Status), TagOf(s.Status), s.Group, s.Name, s.Actual, s.Expected, detail);
            }
            doc.Add(t);
        }

        private void DeviceTable(Document doc, RunReport r)
        {
            var t = DataTable(new float[] { 10, 7, 15, 16, 52 },
                L.T("col.status"), L.T("col.kind"), L.T("col.address"), L.T("col.device"), L.T("col.detail"));
            int row = 0;
            foreach (var d in r.Devices)
                Row(t, row++, ToneOf(d.Status), TagOf(d.Status), d.Kind, d.Address, d.Device, d.Detail);
            doc.Add(t);
        }

        private void HomingTable(Document doc, RunReport r)
        {
            var h = r.Home;
            var t = DataTable(new float[] { 10, 24, 22, 10, 34 },
                L.T("col.status"), L.T("col.programs"), L.T("col.axes"), L.T("col.time"), L.T("col.detail"));
            Row(t, 0, h.Success ? Tone.Pass : Tone.Fail, L.T(h.Success ? "tag.pass" : "tag.fail"),
                h.Programs, string.Join(",", h.Axes), Elapsed(h.Elapsed), h.Detail);
            doc.Add(t);
        }

        private void IoTable(Document doc, RunReport r)
        {
            var t = DataTable(new float[] { 10, 7, 7, 30, 46 },
                L.T("col.status"), L.T("col.op"), L.T("col.in"), L.T("col.name"), L.T("col.detail"));
            int row = 0;
            foreach (var io in r.Io)
            {
                Tone tone = io.Passed ? Tone.Pass : io.Case.Optional ? Tone.Warn : Tone.Fail;
                string tag = L.T(io.Passed ? "tag.pass" : io.Case.Optional ? "tag.warn" : "tag.fail");
                Row(t, row++, tone, tag, io.Case.Out.ToString(), io.Case.In.HasValue ? io.Case.In.Value.ToString() : "?", io.Case.Name, io.Detail);
            }
            doc.Add(t);
        }

        private void DiscoveryTable(Document doc, RunReport r)
        {
            var t = DataTable(new float[] { 10, 7, 15, 20, 48 },
                L.T("col.status"), L.T("col.kind"), L.T("col.address"), L.T("col.device"), L.T("col.detail"));
            int row = 0;

            if (r.DiscoveredIp != null)
                foreach (var e in r.DiscoveredIp.Entries)
                {
                    Tone tone = e.DeviceName == null ? Tone.Neutral
                              : e.Status == ProbeStatus.Ok ? Tone.Pass
                              : e.Status == ProbeStatus.Busy ? Tone.Warn : Tone.Fail;
                    string tag = e.DeviceName == null ? L.T("tag.na")
                               : e.Status == ProbeStatus.Ok ? L.T("tag.pass")
                               : e.Status == ProbeStatus.Busy ? L.T("tag.busy") : L.T("tag.fail");
                    Row(t, row++, tone, tag, "IP", e.Ip, e.DeviceName ?? L.T("sum.nothing"), e.Detail);
                }

            if (r.DiscoveredCom != null)
                foreach (var f in r.DiscoveredCom.Found.OrderBy(x => x.Name))
                    Row(t, row++, Tone.Pass, L.T("tag.pass"), "COM", f.Port, f.Name, "");

            doc.Add(t);

            if (r.DiscoveredCom != null)
            {
                var notes = new List<string> { L.T("sum.itvCount", r.DiscoveredCom.Found.Count(x => x.Name == "ITV regulator")).Trim() };
                if (r.DiscoveredCom.BusyPorts.Count > 0)
                    notes.Add(L.T("sum.comBusy", string.Join(", ", r.DiscoveredCom.BusyPorts)).Trim());
                foreach (var n in notes) doc.Add(new Paragraph(n).SetFontColor(Muted).SetMarginTop(3).SetMarginBottom(0));
            }

            string zmc = r.DiscoveredIp?.ZmcIp;
            if (!string.IsNullOrWhiteSpace(zmc) && zmc != r.Profile.ZmcIp)
                doc.Add(new Paragraph()
                    .Add(Badge(Tone.Warn, L.T("tag.warn"), 7.5f))
                    .Add(new Text("   " + L.T("sum.zmcElsewhere", "", zmc, r.Profile.ZmcIp).Trim()))
                    .SetMarginTop(3));
        }

        private void Result(Document doc, RunVerdict verdict, string path)
        {
            doc.Add(Heading(L.T("pdf.result"), null).SetMarginTop(18));

            var box = new Table(1).UseAllAvailableWidth();
            Color fg = verdict.Passed ? Hex(0x1E7B3C) : Hex(0xB42318);
            Color bg = verdict.Passed ? Hex(0xE3F4E8) : Hex(0xFBE4E4);
            box.AddCell(Plain().SetBackgroundColor(bg).SetBorderLeft(new SolidBorder(fg, 3)).SetPadding(10)
                .Add(new Paragraph(verdict.Passed ? L.T("sum.overallPass") : L.T("sum.overallFail", verdict.Failures))
                    .SetFont(_bold).SetFontSize(13).SetFontColor(fg))
                .Add(Counts(verdict).SetMarginTop(2))
                .Add(new Paragraph(L.T("pdf.file", Path.GetFileName(path))).SetFontColor(Muted).SetMarginTop(2)));
            box.SetKeepTogether(true);
            doc.Add(box);
        }

        private void Footers(Document doc, PdfDocument pdf, string fileName)
        {
            int pages = pdf.GetNumberOfPages();
            for (int i = 1; i <= pages; i++)
            {
                float width = pdf.GetPage(i).GetPageSize().GetWidth();
                doc.ShowTextAligned(new Paragraph($"DeviceScanner  ·  {fileName}").SetFont(_regular).SetFontSize(7).SetFontColor(Muted),
                    PageMargin, 22, i, TextAlignment.LEFT, VerticalAlignment.BOTTOM, 0);
                doc.ShowTextAligned(new Paragraph(L.T("pdf.page", i, pages)).SetFont(_regular).SetFontSize(7).SetFontColor(Muted),
                    width - PageMargin, 22, i, TextAlignment.RIGHT, VerticalAlignment.BOTTOM, 0);
            }
        }

        // ---------- building blocks ----------

        private Paragraph Counts(RunVerdict v)
        {
            string notRun = v.NotRun.Count == 0 ? L.T("pdf.none") : string.Join(", ", v.NotRun);
            return new Paragraph()
                .Add(new Text(L.T("pdf.failures", v.Failures)).SetFont(_bold).SetFontColor(v.Failures > 0 ? Hex(0xB42318) : Ink))
                .Add("     ")
                .Add(new Text(L.T("pdf.warnings", v.Warnings)).SetFont(_bold).SetFontColor(v.Warnings > 0 ? Hex(0x9A6700) : Ink))
                .Add("     ")
                .Add(new Text(L.T("pdf.notRun", notRun)).SetFontColor(Muted))
                .SetMarginBottom(0);
        }

        /// <summary>Section title with the module's badge on the right edge.</summary>
        private Paragraph Heading(string title, ModuleResult m)
        {
            // the badge's tinted background reaches past its text, so stop the tab short of the margin
            float width = iText.Kernel.Geom.PageSize.A4.GetWidth() - 2 * PageMargin - 8.5f * 0.45f - 1;
            var p = new Paragraph()
                .AddTabStops(new TabStop(width, TabAlignment.RIGHT))
                .Add(new Text(title).SetFont(_bold).SetFontSize(12).SetFontColor(Navy))
                .SetBorderBottom(new SolidBorder(NavyLight, 1))
                .SetPaddingBottom(3)
                .SetMarginBottom(3)
                .SetKeepWithNext(true);
            if (m != null) p.Add(new Tab()).Add(Badge(ToneOf(m.Status), TagOf(m.Status), 8.5f));
            return p;
        }

        private Cell Field(string label, string value) => Plain()
            .SetBorderBottom(new SolidBorder(Rule, 0.75f)).SetPaddingBottom(5)
            .Add(new Paragraph(label).SetFontSize(7).SetFontColor(Muted).SetMarginBottom(1))
            .Add(new Paragraph(value).SetFont(_bold).SetFontSize(9));

        private Table DataTable(float[] widths, params string[] headers)
        {
            var t = new Table(UnitValue.CreatePercentArray(widths)).UseAllAvailableWidth().SetMarginTop(4);
            foreach (string h in headers)
                t.AddHeaderCell(Plain().SetBackgroundColor(HeadBg).SetBorderBottom(new SolidBorder(Rule, 0.75f)).SetPadding(4)
                    .Add(new Paragraph(h).SetFont(_bold).SetFontSize(7.5f).SetFontColor(Muted)));
            return t;
        }

        private void Row(Table t, int index, Tone tone, string tag, params string[] cells)
        {
            t.AddCell(BodyCell(index).Add(new Paragraph().Add(Badge(tone, tag, 7))));
            foreach (string c in cells) t.AddCell(BodyCell(index).Add(new Paragraph(c ?? "")));
        }

        private static Cell BodyCell(int row)
        {
            var c = Plain().SetBorderBottom(new SolidBorder(Rule, 0.5f)).SetPadding(3).SetPaddingLeft(4).SetVerticalAlignment(VerticalAlignment.MIDDLE);
            return row % 2 == 1 ? c.SetBackgroundColor(Zebra) : c;
        }

        private static Cell Plain() => new Cell().SetBorder(Border.NO_BORDER);

        /// <summary>A coloured status tag: PASS green, WARN amber, FAIL red, anything else grey.</summary>
        private Text Badge(Tone tone, string text, float size)
        {
            Color fg, bg;
            switch (tone)
            {
                case Tone.Pass: fg = Hex(0x1E7B3C); bg = Hex(0xE3F4E8); break;
                case Tone.Warn: fg = Hex(0x9A6700); bg = Hex(0xFFF3D6); break;
                case Tone.Fail: fg = Hex(0xB42318); bg = Hex(0xFBE4E4); break;
                default: fg = Muted; bg = HeadBg; break;
            }
            return (Text)new Text(text).SetFont(_bold).SetFontSize(size).SetFontColor(fg)
                .SetBackgroundColor(bg, size * 0.45f, size * 0.25f, size * 0.45f, size * 0.25f);
        }

        private static Tone ToneOf(CheckStatus s) =>
            s == CheckStatus.Pass ? Tone.Pass : s == CheckStatus.Unverified ? Tone.Warn : Tone.Fail;

        private static string TagOf(CheckStatus s) =>
            L.T(s == CheckStatus.Pass ? "tag.pass" : s == CheckStatus.Unverified ? "tag.warn" : "tag.fail");

        private static Tone ToneOf(ModuleStatus s) =>
            s == ModuleStatus.Pass ? Tone.Pass : s == ModuleStatus.Warn ? Tone.Warn : s == ModuleStatus.Fail ? Tone.Fail : Tone.Neutral;

        public static string TagOf(ModuleStatus s) =>
            L.T(s == ModuleStatus.Pass ? "tag.pass" : s == ModuleStatus.Warn ? "tag.warn" : s == ModuleStatus.Fail ? "tag.fail" : "tag.notRun");

        private static string Elapsed(TimeSpan t) =>
            t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}:{t.Seconds:00}" : $"{t.TotalSeconds:F1} s";

        private static Color Hex(int rgb) => new DeviceRgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        // ---------- fonts ----------

        /// <summary>
        /// A Windows font with Chinese glyphs (report text and file paths can be Chinese), embedded as a subset.
        /// Falls back to Helvetica if none is installed.
        /// </summary>
        private void LoadFonts()
        {
            string fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            string[][] candidates =
            {
                new[] { "msyh.ttc,0", "msyhbd.ttc,0" },   // Microsoft YaHei + bold
                new[] { "msyh.ttf", "msyhbd.ttf" },
                new[] { "simhei.ttf", null },             // SimHei (no bold file)
                new[] { "simsun.ttc,0", null },           // SimSun
            };
            foreach (var c in candidates)
            {
                var regular = TryFont(fonts, c[0]);
                if (regular == null) continue;
                _regular = regular;
                _bold = (c[1] == null ? null : TryFont(fonts, c[1])) ?? regular;
                return;
            }
            _regular = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            _bold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
        }

        private static PdfFont TryFont(string folder, string spec)
        {
            if (!File.Exists(Path.Combine(folder, spec.Split(',')[0]))) return null;
            try
            {
                return PdfFontFactory.CreateFont(Path.Combine(folder, spec), PdfEncodings.IDENTITY_H,
                                                 PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
            }
            catch { return null; }   // try the next one
        }
    }
}
