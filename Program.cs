using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TevkifatHesaplayici
{
    public enum CalculationMode
    {
        FromKdvDahilTotal = 0,
        FromMatrah = 1,
        FromGenelTotal = 2
    }

    public sealed class CalculationResult
    {
        public decimal InputAmount { get; set; }
        public decimal KdvPercent { get; set; }
        public decimal TevkifatRate { get; set; }
        public string TevkifatDisplay { get; set; }
        public decimal Matrah { get; set; }
        public decimal TahsilEdilecekKdv { get; set; }
        public decimal TevkifEdilenKdv { get; set; }
        public decimal TevkifatliTahsilEdilecekKdv { get; set; }
        public decimal KdvDahilToplam { get; set; }
        public decimal GenelToplam { get; set; }
    }

    public static class InvoiceCalculator
    {
        private static readonly CultureInfo TrCulture = CultureInfo.GetCultureInfo("tr-TR");

        public static bool TryParseAmount(string rawText, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(rawText))
                return false;

            string cleaned = rawText.Trim()
                .Replace("TL", "")
                .Replace("tl", "")
                .Replace("₺", "")
                .Replace(" ", "");

            if (cleaned.Length == 0)
                return false;

            int lastDot = cleaned.LastIndexOf('.');
            int lastComma = cleaned.LastIndexOf(',');

            if (lastDot >= 0 && lastComma >= 0)
            {
                // Whichever separator appears last is treated as decimal separator.
                if (lastComma > lastDot)
                {
                    cleaned = cleaned.Replace(".", "").Replace(',', '.');
                }
                else
                {
                    cleaned = cleaned.Replace(",", "");
                }
            }
            else if (lastComma >= 0)
            {
                int firstComma = cleaned.IndexOf(',');
                if (firstComma == lastComma)
                {
                    cleaned = cleaned.Replace(',', '.');
                }
                else
                {
                    cleaned = cleaned.Replace(",", "");
                }
            }
            else if (lastDot >= 0)
            {
                int firstDot = cleaned.IndexOf('.');
                if (firstDot != lastDot)
                {
                    cleaned = cleaned.Replace(".", "");
                }
            }

            return decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryParseTevkifat(string rawText, out decimal rate, out string displayText)
        {
            rate = 0m;
            displayText = "";
            if (string.IsNullOrWhiteSpace(rawText))
                return false;

            string trimmed = rawText.Trim();
            Match fracMatch = Regex.Match(trimmed, @"(\d+(?:[.,]\d+)?)\s*/\s*(\d+(?:[.,]\d+)?)");
            if (fracMatch.Success)
            {
                decimal num;
                decimal den;
                if (TryParseAmount(fracMatch.Groups[1].Value, out num) &&
                    TryParseAmount(fracMatch.Groups[2].Value, out den) &&
                    den > 0m && num >= 0m && num <= den)
                {
                    rate = num / den;
                    displayText = string.Format(TrCulture, "{0}/{1} (%{2:0.##})",
                        fracMatch.Groups[1].Value,
                        fracMatch.Groups[2].Value,
                        rate * 100m);
                    return true;
                }
                return false;
            }

            bool hasPercent = trimmed.Contains("%");
            string numText = trimmed.Replace("%", "").Trim();
            decimal parsed;
            if (!TryParseAmount(numText, out parsed) || parsed < 0m)
                return false;

            if (hasPercent || parsed > 1m)
            {
                if (parsed > 100m)
                    return false;
                rate = parsed / 100m;
            }
            else
            {
                rate = parsed;
            }

            displayText = string.Format(TrCulture, "%{0:0.##}", rate * 100m);
            return true;
        }

        public static CalculationResult Calculate(decimal amount, decimal kdvPercent, decimal tevkifatRate, string tevkifatDisplay, CalculationMode mode)
        {
            decimal k = kdvPercent / 100m;
            decimal rawMatrah;

            switch (mode)
            {
                case CalculationMode.FromMatrah:
                    rawMatrah = amount;
                    break;
                case CalculationMode.FromGenelTotal:
                    decimal netFactor = 1m + k * (1m - tevkifatRate);
                    rawMatrah = netFactor == 0m ? 0m : amount / netFactor;
                    break;
                case CalculationMode.FromKdvDahilTotal:
                default:
                    decimal grossFactor = 1m + k;
                    rawMatrah = grossFactor == 0m ? 0m : amount / grossFactor;
                    break;
            }

            decimal matrah = RoundMoney(rawMatrah);
            decimal tahsilEdilecekKdv = RoundMoney(matrah * k);
            decimal tevkifEdilenKdv = RoundMoney(tahsilEdilecekKdv * tevkifatRate);
            decimal tevkifatliKdv = tahsilEdilecekKdv - tevkifEdilenKdv;
            decimal kdvDahilToplam = matrah + tahsilEdilecekKdv;
            decimal genelToplam = matrah + tevkifatliKdv;

            return new CalculationResult
            {
                InputAmount = RoundMoney(amount),
                KdvPercent = kdvPercent,
                TevkifatRate = tevkifatRate,
                TevkifatDisplay = tevkifatDisplay,
                Matrah = matrah,
                TahsilEdilecekKdv = tahsilEdilecekKdv,
                TevkifEdilenKdv = tevkifEdilenKdv,
                TevkifatliTahsilEdilecekKdv = tevkifatliKdv,
                KdvDahilToplam = kdvDahilToplam,
                GenelToplam = genelToplam
            };
        }

        public static string FormatCurrency(decimal value)
        {
            return value.ToString("#,##0.00", TrCulture) + " TL";
        }

        public static string FormatPlainNumber(decimal value)
        {
            return value.ToString("0.00", TrCulture);
        }

        private static decimal RoundMoney(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }

    internal sealed class ResultRowControls
    {
        public Label TitleLabel { get; set; }
        public Label ValueLabel { get; set; }
        public Button CopyButton { get; set; }
        public decimal RawValue { get; set; }
    }

    public enum SectorCategory
    {
        Manual = 0,
        KismiHizmet = 1,
        KismiMal = 2,
        TamTevkifat = 3
    }

    public sealed class SectorTevkifatItem
    {
        public string Code { get; set; }
        public string SectorName { get; set; }
        public string TevkifatValue { get; set; }
        public string DefaultKdv { get; set; }
        public SectorCategory Category { get; set; }

        public bool IsManual
        {
            get { return string.IsNullOrEmpty(Code); }
        }

        public override string ToString()
        {
            if (IsManual)
                return SectorName;
            return string.Format("[{0}] {1} — {2}", Code, SectorName, TevkifatValue);
        }

        private static SectorTevkifatItem Entry(string code, string name, string rate, SectorCategory category)
        {
            return new SectorTevkifatItem
            {
                Code = code,
                SectorName = name,
                TevkifatValue = rate,
                DefaultKdv = "20",
                Category = category
            };
        }

        public static SectorTevkifatItem[] GetCatalog()
        {
            return new[]
            {
                Entry("", "Manuel / Özel Oran Seçimi", "", SectorCategory.Manual),

                // Kısmi Tevkifat Kodları (601 - 627)
                Entry("601", "Yapım İşleri ile Bu İşlerle Birlikte İfa Edilen Mühendislik-Mimarlık ve Etüt-Proje Hizmetleri", "4/10 (%40)", SectorCategory.KismiHizmet),
                Entry("602", "Etüt, Plan-Proje, Danışmanlık, Denetim ve Benzeri Hizmetler", "9/10 (%90)", SectorCategory.KismiHizmet),
                Entry("603", "Makine, Teçhizat, Demirbaş ve Taşıtlara Ait Tadil, Bakım ve Onarım Hizmetleri", "7/10 (%70)", SectorCategory.KismiHizmet),
                Entry("604", "Yemek Servis Hizmeti", "5/10 (%50)", SectorCategory.KismiHizmet),
                Entry("605", "Organizasyon Hizmeti", "5/10 (%50)", SectorCategory.KismiHizmet),
                Entry("606", "İşgücü Temin Hizmetleri", "9/10 (%90)", SectorCategory.KismiHizmet),
                Entry("607", "Özel Güvenlik Hizmeti", "9/10 (%90)", SectorCategory.KismiHizmet),
                Entry("608", "Yapı Denetim Hizmetleri", "9/10 (%90)", SectorCategory.KismiHizmet),
                Entry("609", "Fason Olarak Yaptırılan Tekstil ve Konfeksiyon İşleri, Çanta ve Ayakkabı Dikim İşleri ve Bu İşlere Aracılık Hizmetleri", "7/10 (%70)", SectorCategory.KismiHizmet),
                Entry("610", "Turistik Mağazalara Verilen Müşteri Bulma / Götürme Hizmetleri", "9/10 (%90)", SectorCategory.KismiHizmet),
                Entry("611", "Spor Kulüplerinin Yayın, Reklâm ve İsim Hakkı Gelirlerine Konu İşlemleri", "9/10 (%90)", SectorCategory.KismiHizmet),
                Entry("612", "Temizlik Hizmeti", "9/10 (%90)", SectorCategory.KismiHizmet),
                Entry("613", "Çevre ve Bahçe Bakım Hizmetleri", "9/10 (%90)", SectorCategory.KismiHizmet),
                Entry("614", "Servis Taşımacılığı Hizmeti", "5/10 (%50)", SectorCategory.KismiHizmet),
                Entry("615", "Her Türlü Baskı ve Basım Hizmetleri", "7/10 (%70)", SectorCategory.KismiHizmet),
                Entry("616", "5018 Sayılı Kanuna Ekli Cetvellerdeki İdare, Kurum ve Kuruluşlara Yapılan Diğer Hizmetler", "5/10 (%50)", SectorCategory.KismiHizmet),
                Entry("617", "Hurda Metalden Elde Edilen Külçe Teslimleri", "7/10 (%70)", SectorCategory.KismiMal),
                Entry("618", "Hurda Metalden Elde Edilenler Dışındaki Bakır, Çinko, Demir-Çelik, Alüminyum ve Kurşun Külçe Teslimi", "7/10 (%70)", SectorCategory.KismiMal),
                Entry("619", "Bakır, Çinko, Alüminyum ve Kurşun Ürünlerinin Teslimi", "7/10 (%70)", SectorCategory.KismiMal),
                Entry("620", "İstisnadan Vazgeçenlerin Hurda ve Atık Teslimi", "7/10 (%70)", SectorCategory.KismiMal),
                Entry("621", "Metal, Plastik, Lastik, Kauçuk, Kâğıt ve Cam Hurda ve Atıklardan Elde Edilen Hammadde Teslimi", "9/10 (%90)", SectorCategory.KismiMal),
                Entry("622", "Pamuk, Tiftik, Yün ve Yapağı İle Ham Post ve Deri Teslimleri", "9/10 (%90)", SectorCategory.KismiMal),
                Entry("623", "Ağaç ve Orman Ürünleri Teslimi", "5/10 (%50)", SectorCategory.KismiMal),
                Entry("624", "Yük Taşımacılığı Hizmeti", "2/10 (%20)", SectorCategory.KismiHizmet),
                Entry("625", "Ticari Reklam Hizmetleri", "3/10 (%30)", SectorCategory.KismiHizmet),
                Entry("626", "Diğer Teslimler", "2/10 (%20)", SectorCategory.KismiMal),
                Entry("627", "Demir-Çelik Ürünlerinin Teslimi", "5/10 (%50)", SectorCategory.KismiMal),

                // Tam Tevkifat / İsteğe Bağlı Tam Tevkifat Kodları (801 - 825)
                Entry("801", "Yapım İşleri ile Bu İşlerle Birlikte İfa Edilen Mühendislik-Mimarlık ve Etüt-Proje Hizmetleri (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("802", "Etüt, Plan-Proje, Danışmanlık, Denetim ve Benzeri Hizmetler (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("803", "Makine, Teçhizat, Demirbaş ve Taşıtlara Ait Tadil, Bakım ve Onarım Hizmetleri (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("804", "Yemek Servis Hizmeti (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("805", "Organizasyon Hizmeti (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("806", "İşgücü Temin Hizmetleri (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("807", "Özel Güvenlik Hizmeti (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("808", "Yapı Denetim Hizmetleri (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("809", "Fason Olarak Yaptırılan Tekstil ve Konfeksiyon İşleri, Çanta ve Ayakkabı Dikim İşleri (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("810", "Turistik Mağazalara Verilen Müşteri Bulma / Götürme Hizmetleri (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("811", "Spor Kulüplerinin Yayın, Reklâm ve İsim Hakkı Gelirlerine Konu İşlemleri (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("812", "Temizlik Hizmeti (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("813", "Çevre ve Bahçe Bakım Hizmetleri (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("814", "Servis Taşımacılığı Hizmeti (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("815", "Her Türlü Baskı ve Basım Hizmetleri (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("816", "Hurda Metalden Elde Edilen Külçe Teslimleri (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("817", "Hurda Metalden Elde Edilenler Dışındaki Bakır, Çinko, Demir-Çelik, Alüminyum ve Kurşun Külçe Teslimi (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("818", "Bakır, Çinko, Alüminyum ve Kurşun Ürünlerinin Teslimi (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("819", "İstisnadan Vazgeçenlerin Hurda ve Atık Teslimi (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("820", "Metal, Plastik, Lastik, Kauçuk, Kâğıt ve Cam Hurda ve Atıklardan Elde Edilen Hammadde Teslimi (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("821", "Pamuk, Tiftik, Yün ve Yapağı İle Ham Post ve Deri Teslimleri (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("822", "Ağaç ve Orman Ürünleri Teslimi (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("823", "Yük Taşımacılığı Hizmeti (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("824", "Ticari Reklam Hizmetleri (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat),
                Entry("825", "Demir-Çelik Ürünlerinin Teslimi (Tam Tevkifat)", "10/10 (%100)", SectorCategory.TamTevkifat)
            };
        }
    }

    public sealed class MainForm : Form
    {
        private static readonly Color NavyDark = Color.FromArgb(27, 42, 74);
        private static readonly Color AccentBlue = Color.FromArgb(37, 99, 235);
        private static readonly Color BgColor = Color.FromArgb(244, 246, 250);
        private static readonly Color CardBorder = Color.FromArgb(220, 227, 238);
        private static readonly Color TextPrimary = Color.FromArgb(30, 41, 59);
        private static readonly Color TextMuted = Color.FromArgb(100, 116, 139);
        private static readonly Color HighlightBg = Color.FromArgb(238, 244, 255);
        private static readonly Color HighlightText = Color.FromArgb(30, 58, 138);
        private static readonly SectorTevkifatItem[] AllSectors = SectorTevkifatItem.GetCatalog();

        private RadioButton rbFromKdvDahil;
        private RadioButton rbFromMatrah;
        private RadioButton rbFromGenelToplam;

        private TextBox txtAmount;
        private TextBox txtKdvRate;
        private ComboBox cmbSectorGroup;
        private TextBox txtSectorSearch;
        private ComboBox cmbSector;
        private Panel pnlSectorBadge;
        private Label lblSectorInfo;
        private ComboBox cmbTevkifat;
        private Label lblError;
        private Label lblCopyStatus;

        private ResultRowControls rowMatrah;
        private ResultRowControls rowTamKdv;
        private ResultRowControls rowTevkifEdilenKdv;
        private ResultRowControls rowTevkifatliKdv;
        private ResultRowControls rowKdvDahilToplam;
        private ResultRowControls rowGenelToplam;

        private CalculationResult lastResult;
        private Timer statusTimer;
        private bool suppressLiveRecalc;
        private bool suppressSectorSync;
        private bool suppressSectorFilter;

        public MainForm()
        {
            InitializeUi();
            ApplyDefaultValues();
            Recalculate();
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private const int EM_SETCUEBANNER = 0x1501;

        private IntPtr brandIconHandle = IntPtr.Zero;
        private Bitmap brandLogoBitmap;

        private void InitializeUi()
        {
            Text = "EmLz3D AI Center - Tevkifatlı Fatura Hesaplayıcı";
            BackColor = BgColor;
            ForeColor = TextPrimary;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            using (var iconBmp = CreateBrandLogoBitmap(32))
            {
                brandIconHandle = iconBmp.GetHicon();
                Icon = Icon.FromHandle(brandIconHandle);
            }

            FormClosed += delegate
            {
                if (brandLogoBitmap != null)
                {
                    brandLogoBitmap.Dispose();
                    brandLogoBitmap = null;
                }
                if (brandIconHandle != IntPtr.Zero)
                {
                    DestroyIcon(brandIconHandle);
                    brandIconHandle = IntPtr.Zero;
                }
            };

            statusTimer = new Timer { Interval = 1800 };
            statusTimer.Tick += delegate
            {
                statusTimer.Stop();
                lblCopyStatus.Text = "";
            };

            var root = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            root.Controls.Add(CreateHeaderPanel(), 0, 0);
            root.Controls.Add(CreateBodyPanel(), 0, 1);
            Controls.Add(root);
        }

        private Panel CreateHeaderPanel()
        {
            var header = new Panel
            {
                BackColor = Color.FromArgb(15, 23, 42),
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(18, 10, 18, 12),
                Margin = new Padding(0)
            };

            header.Paint += delegate(object sender, PaintEventArgs e)
            {
                var rect = new Rectangle(0, 0, header.Width, header.Height);
                if (rect.Width <= 0 || rect.Height <= 0)
                    return;

                using (var bgBrush = new LinearGradientBrush(
                    rect,
                    Color.FromArgb(15, 23, 42),
                    Color.FromArgb(30, 58, 95),
                    LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillRectangle(bgBrush, rect);
                }

                using (var accentBrush = new LinearGradientBrush(
                    new Rectangle(0, header.Height - 3, header.Width, 3),
                    Color.FromArgb(56, 189, 248),
                    Color.FromArgb(99, 102, 241),
                    LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillRectangle(accentBrush, 0, header.Height - 3, header.Width, 3);
                }
            };

            var headerRow = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };

            brandLogoBitmap = CreateBrandLogoBitmap(58);
            var picLogo = new PictureBox
            {
                Image = brandLogoBitmap,
                Size = new Size(58, 58),
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 2, 14, 0),
                Anchor = AnchorStyles.Left
            };

            var stack = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Anchor = AnchorStyles.Left
            };

            var lblBrand = new Label
            {
                Text = "EmLz3D AI Center",
                ForeColor = Color.FromArgb(103, 232, 249),
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 1)
            };

            var lblTitle = new Label
            {
                Text = "Tevkifatlı Fatura Hesaplama Aracı",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2)
            };

            var lblSub = new Label
            {
                Text = "Matrah, Tahsil Edilecek KDV, Tevkifatlı KDV ve Genel Toplam Hesaplayıcı",
                ForeColor = Color.FromArgb(184, 199, 224),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                AutoSize = true,
                Margin = new Padding(0)
            };

            stack.Controls.Add(lblBrand, 0, 0);
            stack.Controls.Add(lblTitle, 0, 1);
            stack.Controls.Add(lblSub, 0, 2);

            headerRow.Controls.Add(picLogo, 0, 0);
            headerRow.Controls.Add(stack, 1, 0);
            header.Controls.Add(headerRow);
            return header;
        }

        private static Bitmap CreateBrandLogoBitmap(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);

                float scale = size / 64f;
                g.ScaleTransform(scale, scale);

                // Rounded dark badge background
                using (var badgePath = CreateRoundedRectPath(2f, 2f, 60f, 60f, 14f))
                {
                    using (var bgBrush = new LinearGradientBrush(
                        new PointF(0f, 0f),
                        new PointF(64f, 64f),
                        Color.FromArgb(15, 23, 42),
                        Color.FromArgb(30, 41, 82)))
                    {
                        g.FillPath(bgBrush, badgePath);
                    }

                    using (var borderBrush = new LinearGradientBrush(
                        new PointF(0f, 0f),
                        new PointF(64f, 64f),
                        Color.FromArgb(56, 189, 248),
                        Color.FromArgb(129, 140, 248)))
                    using (var borderPen = new Pen(borderBrush, 2.0f))
                    {
                        g.DrawPath(borderPen, badgePath);
                    }
                }

                // Subtle radial AI glow behind cube
                using (var glowBrush = new SolidBrush(Color.FromArgb(40, 56, 189, 248)))
                {
                    g.FillEllipse(glowBrush, 12f, 12f, 40f, 40f);
                }

                // Isometric 3D cube vertices
                var ptTop = new PointF(32f, 14.5f);
                var ptTopRight = new PointF(47.2f, 23.2f);
                var ptBottomRight = new PointF(47.2f, 40.8f);
                var ptBottom = new PointF(32f, 49.5f);
                var ptBottomLeft = new PointF(16.8f, 40.8f);
                var ptTopLeft = new PointF(16.8f, 23.2f);
                var ptCenter = new PointF(32f, 32f);

                // Top face
                using (var topBrush = new LinearGradientBrush(
                    ptTop, ptCenter,
                    Color.FromArgb(235, 103, 232, 249),
                    Color.FromArgb(235, 14, 165, 233)))
                {
                    g.FillPolygon(topBrush, new[] { ptTop, ptTopRight, ptCenter, ptTopLeft });
                }

                // Left face
                using (var leftBrush = new LinearGradientBrush(
                    ptTopLeft, ptBottom,
                    Color.FromArgb(235, 37, 99, 235),
                    Color.FromArgb(235, 30, 64, 175)))
                {
                    g.FillPolygon(leftBrush, new[] { ptTopLeft, ptCenter, ptBottom, ptBottomLeft });
                }

                // Right face
                using (var rightBrush = new LinearGradientBrush(
                    ptTopRight, ptBottom,
                    Color.FromArgb(235, 129, 140, 248),
                    Color.FromArgb(235, 67, 56, 202)))
                {
                    g.FillPolygon(rightBrush, new[] { ptCenter, ptTopRight, ptBottomRight, ptBottom });
                }

                // Cube wireframe & neural connections
                using (var edgePen = new Pen(Color.FromArgb(215, 224, 242, 254), 1.4f))
                {
                    edgePen.LineJoin = LineJoin.Round;
                    g.DrawPolygon(edgePen, new[] { ptTop, ptTopRight, ptBottomRight, ptBottom, ptBottomLeft, ptTopLeft });
                    g.DrawLine(edgePen, ptCenter, ptTopLeft);
                    g.DrawLine(edgePen, ptCenter, ptTopRight);
                    g.DrawLine(edgePen, ptCenter, ptBottom);
                }

                using (var neuralPen = new Pen(Color.FromArgb(145, 165, 243, 252), 1.0f))
                {
                    g.DrawLine(neuralPen, ptCenter, ptTop);
                    g.DrawLine(neuralPen, ptCenter, ptBottomLeft);
                    g.DrawLine(neuralPen, ptCenter, ptBottomRight);
                }

                // Corner AI nodes
                PointF[] outerNodes = { ptTop, ptTopRight, ptBottomRight, ptBottom, ptBottomLeft, ptTopLeft };
                foreach (var node in outerNodes)
                {
                    DrawAiNode(g, node, 2.5f, 1.3f);
                }

                // Central AI core node
                DrawAiNode(g, ptCenter, 4.1f, 2.1f);
            }

            return bmp;
        }

        private static void DrawAiNode(Graphics g, PointF center, float outerRadius, float innerRadius)
        {
            using (var haloBrush = new SolidBrush(Color.FromArgb(220, 56, 189, 248)))
            {
                g.FillEllipse(haloBrush,
                    center.X - outerRadius, center.Y - outerRadius,
                    outerRadius * 2f, outerRadius * 2f);
            }
            using (var coreBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(coreBrush,
                    center.X - innerRadius, center.Y - innerRadius,
                    innerRadius * 2f, innerRadius * 2f);
            }
        }

        private static GraphicsPath CreateRoundedRectPath(float x, float y, float width, float height, float radius)
        {
            float d = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(x, y, d, d, 180f, 90f);
            path.AddArc(x + width - d, y, d, d, 270f, 90f);
            path.AddArc(x + width - d, y + height - d, d, d, 0f, 90f);
            path.AddArc(x, y + height - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        private Control CreateBodyPanel()
        {
            var body = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(16, 10, 16, 10),
                Margin = new Padding(0),
                BackColor = BgColor
            };

            body.Controls.Add(CreateModeCard(), 0, 0);
            body.Controls.Add(CreateInputCard(), 0, 1);
            body.Controls.Add(CreateResultCard(), 0, 2);
            return body;
        }

        private Panel CreateCardPanel(Padding innerPadding)
        {
            var card = new Panel
            {
                BackColor = Color.White,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = innerPadding,
                Margin = new Padding(0, 0, 0, 8)
            };

            card.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(CardBorder, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                }
            };

            return card;
        }

        private static Label CreateSectionTitle(string text)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = NavyDark,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 5)
            };
        }

        private Control CreateModeCard()
        {
            var card = CreateCardPanel(new Padding(16, 9, 16, 8));
            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 4,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };

            rbFromKdvDahil = new RadioButton
            {
                Text = "KDV Dahil Toplam Tutardan (Tevkifat Öncesi Toplam)",
                AutoSize = true,
                Checked = true,
                Margin = new Padding(2, 1, 0, 2)
            };
            rbFromMatrah = new RadioButton
            {
                Text = "KDV Hariç Tutardan (Matrah / Mal-Hizmet Toplamı)",
                AutoSize = true,
                Margin = new Padding(2, 1, 0, 2)
            };
            rbFromGenelToplam = new RadioButton
            {
                Text = "Tevkifatlı Genel Toplamdan (Ödenecek Net Tutar)",
                AutoSize = true,
                Margin = new Padding(2, 1, 0, 1)
            };

            EventHandler modeChanged = delegate
            {
                if (!suppressLiveRecalc)
                    Recalculate();
            };
            rbFromKdvDahil.CheckedChanged += modeChanged;
            rbFromMatrah.CheckedChanged += modeChanged;
            rbFromGenelToplam.CheckedChanged += modeChanged;

            layout.Controls.Add(CreateSectionTitle("Hesaplama Yöntemi (Girilen Tutar Türü)"), 0, 0);
            layout.Controls.Add(rbFromKdvDahil, 0, 1);
            layout.Controls.Add(rbFromMatrah, 0, 2);
            layout.Controls.Add(rbFromGenelToplam, 0, 3);

            card.Controls.Add(layout);
            return card;
        }

        private Control CreateInputCard()
        {
            var card = CreateCardPanel(new Padding(16, 10, 16, 10));
            var outer = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 4,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };

            outer.Controls.Add(CreateSectionTitle("Fatura Giriş Bilgileri"), 0, 0);

            var grid = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 6,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // Row 0: Tutar
            grid.Controls.Add(CreateFieldLabel("Toplam Tutar (TL):"), 0, 0);
            txtAmount = new TextBox
            {
                Width = 140,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Right,
                Margin = new Padding(0, 2, 10, 4)
            };
            txtAmount.TextChanged += delegate
            {
                if (!suppressLiveRecalc)
                    Recalculate();
            };
            grid.Controls.Add(txtAmount, 1, 0);
            grid.Controls.Add(new Label
            {
                Text = "Örn: 12000 veya 12000,50 (nokta/virgül destekli)",
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 3, 0, 4)
            }, 2, 0);

            // Row 1: KDV Oranı
            grid.Controls.Add(CreateFieldLabel("KDV Yüzdesi (%):"), 0, 1);
            txtKdvRate = new TextBox
            {
                Width = 140,
                Font = new Font("Segoe UI", 10f),
                TextAlign = HorizontalAlignment.Right,
                Margin = new Padding(0, 2, 10, 4)
            };
            txtKdvRate.TextChanged += delegate
            {
                if (!suppressLiveRecalc)
                    Recalculate();
            };
            grid.Controls.Add(txtKdvRate, 1, 1);

            var kdvPresets = CreatePresetFlow(
                new[] { "%1", "%10", "%20" },
                new[] { "1", "10", "20" },
                delegate(string val) { txtKdvRate.Text = val; });
            grid.Controls.Add(kdvPresets, 2, 1);

            // Row 2: GİB Grup Filtresi + Hızlı Arama
            grid.Controls.Add(CreateFieldLabel("GİB Grup / Arama:"), 0, 2);
            var filterRow = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 4, 4)
            };
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64f));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36f));

            cmbSectorGroup = new ComboBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f),
                DropDownStyle = ComboBoxStyle.DropDownList,
                DropDownWidth = 390,
                Margin = new Padding(0, 0, 6, 0)
            };
            cmbSectorGroup.Items.AddRange(new object[]
            {
                "Tümü: Kısmi (601-627) & Tam (801-825) [52 Kod]",
                "Kısmi Tevkifat — Hizmet İşlemleri (601-616, 624-625)",
                "Kısmi Tevkifat — Mal Teslimleri (617-623, 626-627)",
                "Tam Tevkifat — İsteğe Bağlı / 10/10 İşlemler (801-825)"
            });
            cmbSectorGroup.SelectedIndexChanged += delegate
            {
                if (!suppressSectorFilter)
                    RefreshSectorList(false);
            };

            txtSectorSearch = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.2f),
                Margin = new Padding(0)
            };
            txtSectorSearch.HandleCreated += delegate
            {
                SendMessage(txtSectorSearch.Handle, EM_SETCUEBANNER, IntPtr.Zero, "Kod / kelime ara (ör. 609, tekstil)");
            };
            txtSectorSearch.TextChanged += delegate
            {
                if (!suppressSectorFilter)
                    RefreshSectorList(!string.IsNullOrWhiteSpace(txtSectorSearch.Text));
            };

            filterRow.Controls.Add(cmbSectorGroup, 0, 0);
            filterRow.Controls.Add(txtSectorSearch, 1, 0);
            grid.Controls.Add(filterRow, 1, 2);
            grid.SetColumnSpan(filterRow, 2);

            // Row 3: Sektör / İşlem Türü (GİB Kodlu)
            grid.Controls.Add(CreateFieldLabel("Sektör / İşlem:"), 0, 3);
            cmbSector = new ComboBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.2f),
                DropDownStyle = ComboBoxStyle.DropDownList,
                DropDownWidth = 680,
                MaxDropDownItems = 18,
                Margin = new Padding(0, 2, 4, 4)
            };
            cmbSector.Items.AddRange(AllSectors);
            cmbSector.SelectedIndexChanged += delegate { OnSectorChanged(); };
            grid.Controls.Add(cmbSector, 1, 3);
            grid.SetColumnSpan(cmbSector, 2);

            // Row 4: GİB Bilgi Rozeti
            var badgePanel = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 1,
                BackColor = Color.FromArgb(241, 245, 249),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 5, 8, 5),
                Margin = new Padding(0, 0, 4, 6)
            };
            pnlSectorBadge = badgePanel;
            pnlSectorBadge.Paint += delegate(object sender, PaintEventArgs e)
            {
                var borderColor = (cmbSector != null && cmbSector.SelectedItem is SectorTevkifatItem && !((SectorTevkifatItem)cmbSector.SelectedItem).IsManual)
                    ? Color.FromArgb(147, 197, 253)
                    : CardBorder;
                using (var pen = new Pen(borderColor, 1f))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, pnlSectorBadge.Width - 1, pnlSectorBadge.Height - 1);
                }
            };
            lblSectorInfo = new Label
            {
                Text = "Manuel seçim: 52 GİB tevkifat kodu arasından seçebilir veya özel oran girebilirsiniz.",
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                AutoSize = true,
                Margin = new Padding(0)
            };
            badgePanel.Controls.Add(lblSectorInfo, 0, 0);
            grid.Controls.Add(pnlSectorBadge, 1, 4);
            grid.SetColumnSpan(pnlSectorBadge, 2);

            // Row 5: Tevkifat Oranı
            grid.Controls.Add(CreateFieldLabel("Tevkifat Oranı:"), 0, 5);
            cmbTevkifat = new ComboBox
            {
                Width = 140,
                Font = new Font("Segoe UI", 9.5f),
                DropDownStyle = ComboBoxStyle.DropDown,
                Margin = new Padding(0, 3, 10, 6)
            };
            cmbTevkifat.Items.AddRange(new object[]
            {
                "2/10 (%20)",
                "3/10 (%30)",
                "4/10 (%40)",
                "5/10 (%50)",
                "7/10 (%70)",
                "9/10 (%90)",
                "10/10 (%100)"
            });
            cmbTevkifat.TextChanged += delegate { OnTevkifatInputChanged(); };
            cmbTevkifat.SelectedIndexChanged += delegate { OnTevkifatInputChanged(); };
            grid.Controls.Add(cmbTevkifat, 1, 5);

            var tevLabels = new[] { "2/10", "3/10", "4/10", "5/10", "7/10", "9/10", "10/10" };
            var tevValues = new[]
            {
                "2/10 (%20)",
                "3/10 (%30)",
                "4/10 (%40)",
                "5/10 (%50)",
                "7/10 (%70)",
                "9/10 (%90)",
                "10/10 (%100)"
            };
            var tevPresets = CreatePresetFlow(tevLabels, tevValues, delegate(string val)
            {
                cmbTevkifat.Text = val;
            });
            grid.Controls.Add(tevPresets, 2, 5);

            outer.Controls.Add(grid, 0, 1);

            // Action bar
            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 4, 0, 0)
            };

            var btnCalc = new Button
            {
                Text = "Hesapla",
                BackColor = AccentBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Size = new Size(110, 30),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0)
            };
            btnCalc.FlatAppearance.BorderSize = 0;
            btnCalc.Click += delegate { Recalculate(); };
            AcceptButton = btnCalc;

            var btnClear = new Button
            {
                Text = "Temizle",
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = TextPrimary,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f),
                Size = new Size(95, 30),
                Cursor = Cursors.Hand,
                Margin = new Padding(0)
            };
            btnClear.FlatAppearance.BorderSize = 0;
            btnClear.Click += delegate { ClearInputs(); };

            actions.Controls.Add(btnCalc);
            actions.Controls.Add(btnClear);
            outer.Controls.Add(actions, 0, 2);

            lblError = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(185, 28, 28),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                AutoSize = true,
                Visible = false,
                Margin = new Padding(0, 4, 0, 0)
            };
            outer.Controls.Add(lblError, 0, 3);

            card.Controls.Add(outer);
            return card;
        }

        private Control CreateResultCard()
        {
            var card = CreateCardPanel(new Padding(16, 10, 16, 10));
            card.Margin = new Padding(0);

            var outer = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };

            outer.Controls.Add(CreateSectionTitle("Fatura Hesaplama Sonuçları"), 0, 0);

            var table = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 6,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));

            rowMatrah = AddResultRow(table, 0, "Matrah (KDV Hariç Tutar):", false);
            rowTamKdv = AddResultRow(table, 1, "Tahsil Edilecek KDV (Tam KDV):", false);
            rowTevkifEdilenKdv = AddResultRow(table, 2, "Tevkif Edilen KDV (Tevkifat Tutarı):", false);
            rowTevkifatliKdv = AddResultRow(table, 3, "Tevkifatlı Tahsil Edilecek KDV:", false);
            rowKdvDahilToplam = AddResultRow(table, 4, "KDV Dahil Toplam (Tevkifatsız):", false);
            rowGenelToplam = AddResultRow(table, 5, "Genel Toplam (Ödenecek Tutar):", true);

            outer.Controls.Add(table, 0, 1);

            var footer = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            lblCopyStatus = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(21, 128, 61),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 0, 0)
            };

            var btnCopyAll = new Button
            {
                Text = "Fatura Özetini Kopyala",
                BackColor = NavyDark,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Size = new Size(175, 30),
                Cursor = Cursors.Hand,
                Margin = new Padding(0)
            };
            btnCopyAll.FlatAppearance.BorderSize = 0;
            btnCopyAll.Click += delegate { CopySummaryToClipboard(); };

            footer.Controls.Add(lblCopyStatus, 0, 0);
            footer.Controls.Add(btnCopyAll, 1, 0);
            outer.Controls.Add(footer, 0, 2);

            card.Controls.Add(outer);
            return card;
        }

        private ResultRowControls AddResultRow(TableLayoutPanel table, int rowIndex, string title, bool highlight)
        {
            Color bg = highlight ? HighlightBg : Color.White;
            Color fg = highlight ? HighlightText : TextPrimary;
            Font labelFont = highlight
                ? new Font("Segoe UI", 10f, FontStyle.Bold)
                : new Font("Segoe UI", 9.5f, FontStyle.Regular);
            Font valueFont = highlight
                ? new Font("Segoe UI", 11.5f, FontStyle.Bold)
                : new Font("Segoe UI", 10f, FontStyle.Bold);

            var titlePanel = new Panel
            {
                BackColor = bg,
                Dock = DockStyle.Fill,
                Height = highlight ? 32 : 26,
                Margin = new Padding(0, 1, 0, 1),
                Padding = new Padding(8, 0, 6, 0)
            };
            var lblTitle = new Label
            {
                Text = title,
                Font = labelFont,
                ForeColor = fg,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            titlePanel.Controls.Add(lblTitle);

            var valuePanel = new Panel
            {
                BackColor = bg,
                Dock = DockStyle.Fill,
                Height = highlight ? 32 : 26,
                Margin = new Padding(0, 1, 8, 1),
                Padding = new Padding(4, 0, 8, 0)
            };
            var lblValue = new Label
            {
                Text = "0,00 TL",
                Font = valueFont,
                ForeColor = fg,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight
            };
            valuePanel.Controls.Add(lblValue);

            var btnCopy = new Button
            {
                Text = "Kopyala",
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = TextPrimary,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f),
                Size = new Size(76, highlight ? 28 : 24),
                Anchor = AnchorStyles.Right,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 1, 0, 1)
            };
            btnCopy.FlatAppearance.BorderColor = CardBorder;
            btnCopy.FlatAppearance.BorderSize = 1;

            var row = new ResultRowControls
            {
                TitleLabel = lblTitle,
                ValueLabel = lblValue,
                CopyButton = btnCopy
            };

            btnCopy.Click += delegate
            {
                if (lastResult == null)
                    return;
                string plain = InvoiceCalculator.FormatPlainNumber(row.RawValue);
                CopyTextToClipboard(plain, title.TrimEnd(':') + " (" + plain + ")");
            };

            table.Controls.Add(titlePanel, 0, rowIndex);
            table.Controls.Add(valuePanel, 1, rowIndex);
            table.Controls.Add(btnCopy, 2, rowIndex);
            return row;
        }

        private static Label CreateFieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = TextPrimary,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 4, 8, 6)
            };
        }

        private static FlowLayoutPanel CreatePresetFlow(string[] labels, string[] values, Action<string> onSelect)
        {
            var flow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 2, 0, 4),
                WrapContents = false
            };

            for (int i = 0; i < labels.Length; i++)
            {
                string targetValue = values[i];
                var btn = new Button
                {
                    Text = labels[i],
                    BackColor = Color.FromArgb(241, 245, 249),
                    ForeColor = NavyDark,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 8.5f),
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Padding = new Padding(4, 1, 4, 1),
                    MinimumSize = new Size(38, 25),
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0, 0, 4, 0)
                };
                btn.FlatAppearance.BorderColor = CardBorder;
                btn.FlatAppearance.BorderSize = 1;
                btn.Click += delegate { onSelect(targetValue); };
                flow.Controls.Add(btn);
            }

            return flow;
        }

        private void ApplyDefaultValues()
        {
            suppressLiveRecalc = true;
            suppressSectorSync = true;
            suppressSectorFilter = true;
            rbFromKdvDahil.Checked = true;
            txtAmount.Text = "1200";
            txtKdvRate.Text = "20";
            cmbSectorGroup.SelectedIndex = 0;
            txtSectorSearch.Text = "";
            suppressSectorFilter = false;
            RefreshSectorList(false);
            cmbSector.SelectedIndex = 0;
            cmbTevkifat.Text = "5/10 (%50)";
            UpdateSectorBadge(cmbSector.SelectedItem as SectorTevkifatItem);
            suppressSectorSync = false;
            suppressLiveRecalc = false;
        }

        private void ClearInputs()
        {
            suppressLiveRecalc = true;
            suppressSectorSync = true;
            suppressSectorFilter = true;
            txtAmount.Text = "";
            txtKdvRate.Text = "20";
            cmbSectorGroup.SelectedIndex = 0;
            txtSectorSearch.Text = "";
            suppressSectorFilter = false;
            RefreshSectorList(false);
            cmbSector.SelectedIndex = 0;
            cmbTevkifat.Text = "5/10 (%50)";
            UpdateSectorBadge(cmbSector.SelectedItem as SectorTevkifatItem);
            suppressSectorSync = false;
            suppressLiveRecalc = false;

            lastResult = null;
            lblError.Visible = false;
            SetZeroResults();
            txtAmount.Focus();
        }

        private void RefreshSectorList(bool selectFirstMatch)
        {
            var previous = cmbSector.SelectedItem as SectorTevkifatItem;
            SectorCategory? filterCategory = null;
            if (cmbSectorGroup.SelectedIndex == 1)
                filterCategory = SectorCategory.KismiHizmet;
            else if (cmbSectorGroup.SelectedIndex == 2)
                filterCategory = SectorCategory.KismiMal;
            else if (cmbSectorGroup.SelectedIndex == 3)
                filterCategory = SectorCategory.TamTevkifat;

            string query = txtSectorSearch.Text == null ? "" : txtSectorSearch.Text.Trim();
            CompareInfo trCompare = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;

            var filtered = new List<SectorTevkifatItem>();
            filtered.Add(AllSectors[0]);

            for (int i = 1; i < AllSectors.Length; i++)
            {
                SectorTevkifatItem item = AllSectors[i];
                if (filterCategory.HasValue && item.Category != filterCategory.Value)
                    continue;

                if (!string.IsNullOrEmpty(query))
                {
                    bool matched =
                        trCompare.IndexOf(item.Code, query, CompareOptions.IgnoreCase) >= 0 ||
                        trCompare.IndexOf(item.SectorName, query, CompareOptions.IgnoreCase) >= 0 ||
                        trCompare.IndexOf(item.TevkifatValue, query, CompareOptions.IgnoreCase) >= 0;
                    if (!matched)
                        continue;
                }

                filtered.Add(item);
            }

            suppressSectorSync = true;
            cmbSector.BeginUpdate();
            cmbSector.Items.Clear();
            cmbSector.Items.AddRange(filtered.ToArray());

            bool triggerSectorApply = false;
            if (selectFirstMatch && filtered.Count > 1)
            {
                cmbSector.SelectedIndex = 1;
                triggerSectorApply = true;
            }
            else if (previous != null && !previous.IsManual && filtered.Contains(previous))
            {
                cmbSector.SelectedItem = previous;
            }
            else
            {
                cmbSector.SelectedIndex = 0;
            }
            cmbSector.EndUpdate();
            suppressSectorSync = false;

            if (triggerSectorApply)
            {
                OnSectorChanged();
            }
            else
            {
                UpdateSectorBadge(cmbSector.SelectedItem as SectorTevkifatItem);
            }
        }

        private void OnSectorChanged()
        {
            if (suppressSectorSync)
                return;

            var selected = cmbSector.SelectedItem as SectorTevkifatItem;
            UpdateSectorBadge(selected);
            if (selected == null || selected.IsManual)
                return;

            suppressSectorSync = true;
            suppressLiveRecalc = true;
            if (string.IsNullOrWhiteSpace(txtKdvRate.Text))
                txtKdvRate.Text = selected.DefaultKdv;
            cmbTevkifat.Text = selected.TevkifatValue;
            suppressLiveRecalc = false;
            suppressSectorSync = false;

            Recalculate();
        }

        private void OnTevkifatInputChanged()
        {
            if (!suppressSectorSync)
            {
                var selected = cmbSector.SelectedItem as SectorTevkifatItem;
                if (selected != null && !selected.IsManual)
                {
                    decimal currentRate;
                    string currentDisp;
                    decimal sectorRate;
                    string sectorDisp;
                    bool currentOk = InvoiceCalculator.TryParseTevkifat(cmbTevkifat.Text, out currentRate, out currentDisp);
                    bool sectorOk = InvoiceCalculator.TryParseTevkifat(selected.TevkifatValue, out sectorRate, out sectorDisp);

                    if (!currentOk || !sectorOk || currentRate != sectorRate)
                    {
                        suppressSectorSync = true;
                        cmbSector.SelectedIndex = 0;
                        UpdateSectorBadge(cmbSector.SelectedItem as SectorTevkifatItem);
                        suppressSectorSync = false;
                    }
                }
            }

            if (!suppressLiveRecalc)
                Recalculate();
        }

        private void UpdateSectorBadge(SectorTevkifatItem item)
        {
            if (item == null || item.IsManual)
            {
                pnlSectorBadge.BackColor = Color.FromArgb(241, 245, 249);
                lblSectorInfo.ForeColor = TextMuted;
                lblSectorInfo.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
                int matchCount = cmbSector != null ? Math.Max(0, cmbSector.Items.Count - 1) : 52;
                lblSectorInfo.Text = matchCount == 0
                    ? "Aramanızla eşleşen GİB kodu bulunamadı — manuel seçim aktif."
                    : string.Format("Manuel seçim ({0} GİB kodu listeleniyor): Yukarıdan sektör seçebilir veya özel oran girebilirsiniz.", matchCount);
            }
            else
            {
                pnlSectorBadge.BackColor = HighlightBg;
                lblSectorInfo.ForeColor = HighlightText;
                lblSectorInfo.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                string kindText = item.Category == SectorCategory.TamTevkifat ? "Tam Tevkifat" : "Kısmi Tevkifat";
                lblSectorInfo.Text = string.Format("GİB Kodu: {0} ({1})  •  Uygulanan Tevkifat Oranı: {2}", item.Code, kindText, item.TevkifatValue);
            }
            pnlSectorBadge.Invalidate();
        }

        private CalculationMode GetSelectedMode()
        {
            if (rbFromMatrah.Checked)
                return CalculationMode.FromMatrah;
            if (rbFromGenelToplam.Checked)
                return CalculationMode.FromGenelTotal;
            return CalculationMode.FromKdvDahilTotal;
        }

        private void Recalculate()
        {
            if (string.IsNullOrWhiteSpace(txtAmount.Text))
            {
                lastResult = null;
                lblError.Visible = false;
                SetZeroResults();
                return;
            }

            decimal amount;
            if (!InvoiceCalculator.TryParseAmount(txtAmount.Text, out amount) || amount < 0m)
            {
                ShowInputError("Geçerli ve sıfırdan büyük veya eşit bir tutar giriniz.");
                return;
            }

            decimal kdvPercent;
            if (!InvoiceCalculator.TryParseAmount(txtKdvRate.Text, out kdvPercent) || kdvPercent < 0m || kdvPercent > 100m)
            {
                ShowInputError("KDV yüzdesi 0 ile 100 arasında olmalıdır (ör. 1, 10, 20).");
                return;
            }

            decimal tevkifatRate;
            string tevkifatDisplay;
            if (!InvoiceCalculator.TryParseTevkifat(cmbTevkifat.Text, out tevkifatRate, out tevkifatDisplay))
            {
                ShowInputError("Tevkifat oranı geçersiz (ör. 2/10, 5/10, 9/10 veya %50).");
                return;
            }

            lblError.Visible = false;
            lastResult = InvoiceCalculator.Calculate(amount, kdvPercent, tevkifatRate, tevkifatDisplay, GetSelectedMode());

            UpdateRow(rowMatrah, lastResult.Matrah);
            UpdateRow(rowTamKdv, lastResult.TahsilEdilecekKdv);
            UpdateRow(rowTevkifEdilenKdv, lastResult.TevkifEdilenKdv);
            UpdateRow(rowTevkifatliKdv, lastResult.TevkifatliTahsilEdilecekKdv);
            UpdateRow(rowKdvDahilToplam, lastResult.KdvDahilToplam);
            UpdateRow(rowGenelToplam, lastResult.GenelToplam);
        }

        private void ShowInputError(string message)
        {
            lastResult = null;
            lblError.Text = message;
            lblError.Visible = true;
            SetZeroResults();
        }

        private void SetZeroResults()
        {
            UpdateRow(rowMatrah, 0m);
            UpdateRow(rowTamKdv, 0m);
            UpdateRow(rowTevkifEdilenKdv, 0m);
            UpdateRow(rowTevkifatliKdv, 0m);
            UpdateRow(rowKdvDahilToplam, 0m);
            UpdateRow(rowGenelToplam, 0m);
        }

        private static void UpdateRow(ResultRowControls row, decimal value)
        {
            row.RawValue = value;
            row.ValueLabel.Text = InvoiceCalculator.FormatCurrency(value);
        }

        private void CopySummaryToClipboard()
        {
            if (lastResult == null)
            {
                ShowCopyStatus("Kopyalanacak geçerli bir hesaplama yok.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== Tevkifatlı Fatura Hesaplama Özeti ===");
            var selectedSector = cmbSector.SelectedItem as SectorTevkifatItem;
            if (selectedSector != null && !selectedSector.IsManual)
            {
                sb.AppendLine("Sektör / İşlem (GİB " + selectedSector.Code + "): " + selectedSector.SectorName);
            }
            sb.AppendLine("KDV Oranı: %" + lastResult.KdvPercent.ToString("0.##", CultureInfo.GetCultureInfo("tr-TR")));
            sb.AppendLine("Tevkifat Oranı: " + lastResult.TevkifatDisplay);
            sb.AppendLine("-----------------------------------------");
            sb.AppendLine("Matrah (KDV Hariç): " + InvoiceCalculator.FormatCurrency(lastResult.Matrah));
            sb.AppendLine("Tahsil Edilecek KDV (Tam): " + InvoiceCalculator.FormatCurrency(lastResult.TahsilEdilecekKdv));
            sb.AppendLine("Tevkif Edilen KDV: " + InvoiceCalculator.FormatCurrency(lastResult.TevkifEdilenKdv));
            sb.AppendLine("Tevkifatlı Tahsil Edilecek KDV: " + InvoiceCalculator.FormatCurrency(lastResult.TevkifatliTahsilEdilecekKdv));
            sb.AppendLine("KDV Dahil Toplam: " + InvoiceCalculator.FormatCurrency(lastResult.KdvDahilToplam));
            sb.AppendLine("Genel Toplam (Ödenecek): " + InvoiceCalculator.FormatCurrency(lastResult.GenelToplam));

            CopyTextToClipboard(sb.ToString(), "Fatura özeti panoya kopyalandı.");
        }

        private void CopyTextToClipboard(string text, string statusMessage)
        {
            try
            {
                Clipboard.SetText(text);
                ShowCopyStatus("Kopyalandı: " + statusMessage);
            }
            catch (Exception ex)
            {
                ShowCopyStatus("Panoya kopyalanamadı: " + ex.Message);
            }
        }

        private void ShowCopyStatus(string message)
        {
            lblCopyStatus.Text = message;
            statusTimer.Stop();
            statusTimer.Start();
        }
    }

    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            if (Environment.OSVersion.Version.Major >= 6)
                SetProcessDPIAware();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
