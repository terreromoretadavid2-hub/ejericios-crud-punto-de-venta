// Punto de Venta - C# WinForms + SQL Server. Compatible con C# 5 (csc.exe incluido en Windows).
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace PuntoDeVenta
{
    // ---------- Modelo ----------
    public class User { public string U; public string N; public string P; public string R; }
    public class Product { public string C; public string N; public decimal Pr; public int St; }
    public class Item { public string C; public string N; public decimal Pr; public int Q; }
    public class Invoice
    {
        public int No; public DateTime D; public string U; public string Cn; public string Cr; public string Pm;
        public decimal Sub; public decimal Dc; public decimal Itb; public decimal Total; public decimal Rv;
        public List<Item> Items = new List<Item>();
    }
    public class Db
    {
        public string Biz = "Mi Negocio"; public int Seq = 1;
        public List<User> Users = new List<User>();
        public List<Product> Products = new List<Product>();
        public List<Invoice> Invoices = new List<Invoice>();
    }

    static class Store
    {
        public static Db D;
        public const decimal ITBIS = 0.18m;
        // Cadena de conexion por defecto. Se puede cambiar en el archivo conexion.txt (junto al .exe).
        static string cs = "Server=localhost\\SQLEXPRESS;Database=PuntoDeVenta;Integrated Security=True;";

        static SqlCommand Cmd(SqlConnection c, SqlTransaction t, string sql, params object[] p)
        {
            var m = new SqlCommand(sql, c, t);
            for (int i = 0; i + 1 < p.Length; i += 2) m.Parameters.AddWithValue((string)p[i], p[i + 1] ?? DBNull.Value);
            return m;
        }
        static void Exec(SqlConnection c, SqlTransaction t, string sql, params object[] p)
        {
            using (var m = Cmd(c, t, sql, p)) m.ExecuteNonQuery();
        }

        public static void Load()
        {
            string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "conexion.txt");
            if (File.Exists(f)) { string t = File.ReadAllText(f).Trim(); if (t.Length > 0) cs = t; }
            D = new Db();
            using (var c = new SqlConnection(cs))
            {
                c.Open();
                Exec(c, null, "IF OBJECT_ID('Config') IS NULL CREATE TABLE Config(Clave VARCHAR(50) PRIMARY KEY, Valor VARCHAR(200) NOT NULL)");
                using (var r = Cmd(c, null, "SELECT Usuario,Nombre,Clave,Rol FROM Usuarios").ExecuteReader())
                    while (r.Read()) D.Users.Add(new User { U = r.GetString(0), N = r.GetString(1), P = r.GetString(2).Trim(), R = r.GetString(3) });
                using (var r = Cmd(c, null, "SELECT Codigo,Nombre,Precio,Existencia FROM Productos ORDER BY Nombre").ExecuteReader())
                    while (r.Read()) D.Products.Add(new Product { C = r.GetString(0), N = r.GetString(1), Pr = r.GetDecimal(2), St = r.GetInt32(3) });
                var map = new Dictionary<int, Invoice>();
                using (var r = Cmd(c, null, "SELECT No,Fecha,Cajero,Cliente,RncCedula,FormaPago,Subtotal,Descuento,Itbis,Total,Recibido FROM Facturas ORDER BY No DESC").ExecuteReader())
                    while (r.Read())
                    {
                        var v = new Invoice();
                        v.No = r.GetInt32(0); v.D = r.GetDateTime(1); v.U = r.GetString(2); v.Cn = r.GetString(3);
                        v.Cr = r.IsDBNull(4) ? "" : r.GetString(4); v.Pm = r.GetString(5);
                        v.Sub = r.GetDecimal(6); v.Dc = r.GetDecimal(7); v.Itb = r.GetDecimal(8); v.Total = r.GetDecimal(9); v.Rv = r.GetDecimal(10);
                        D.Invoices.Add(v); map[v.No] = v;
                    }
                using (var r = Cmd(c, null, "SELECT FacturaNo,Codigo,Articulo,Precio,Cantidad FROM FacturaDetalle ORDER BY Id").ExecuteReader())
                    while (r.Read())
                    {
                        Invoice v;
                        if (map.TryGetValue(r.GetInt32(0), out v))
                            v.Items.Add(new Item { C = r.GetString(1), N = r.GetString(2), Pr = r.GetDecimal(3), Q = r.GetInt32(4) });
                    }
                object biz = Cmd(c, null, "SELECT Valor FROM Config WHERE Clave='Biz'").ExecuteScalar();
                if (biz != null) D.Biz = Convert.ToString(biz);
            }
            D.Seq = (D.Invoices.Count > 0 ? D.Invoices[0].No : 0) + 1;
        }

        // Sincroniza con SQL Server: usuarios y productos se reescriben; las facturas nuevas se insertan.
        public static void Save()
        {
            try
            {
                using (var c = new SqlConnection(cs))
                {
                    c.Open();
                    using (var t = c.BeginTransaction())
                    {
                        try
                        {
                            Exec(c, t, "DELETE FROM Usuarios");
                            foreach (var u in D.Users)
                                Exec(c, t, "INSERT INTO Usuarios(Usuario,Nombre,Clave,Rol) VALUES(@a,@b,@c,@d)", "@a", u.U, "@b", u.N, "@c", u.P, "@d", u.R);
                            Exec(c, t, "DELETE FROM Productos");
                            foreach (var p in D.Products)
                                Exec(c, t, "INSERT INTO Productos(Codigo,Nombre,Precio,Existencia) VALUES(@a,@b,@c,@d)", "@a", p.C, "@b", p.N, "@c", p.Pr, "@d", p.St);
                            int max = Convert.ToInt32(Cmd(c, t, "SELECT ISNULL(MAX(No),0) FROM Facturas").ExecuteScalar());
                            foreach (var v in D.Invoices)
                            {
                                if (v.No <= max) continue;
                                Exec(c, t, "SET IDENTITY_INSERT Facturas ON; INSERT INTO Facturas(No,Fecha,Cajero,Cliente,RncCedula,FormaPago,Subtotal,Descuento,Itbis,Total,Recibido) VALUES(@a,@b,@c,@d,@e,@f,@g,@h,@i,@j,@k); SET IDENTITY_INSERT Facturas OFF;",
                                    "@a", v.No, "@b", v.D, "@c", v.U, "@d", v.Cn, "@e", v.Cr, "@f", v.Pm, "@g", v.Sub, "@h", v.Dc, "@i", v.Itb, "@j", v.Total, "@k", v.Rv);
                                foreach (var i in v.Items)
                                    Exec(c, t, "INSERT INTO FacturaDetalle(FacturaNo,Codigo,Articulo,Precio,Cantidad) VALUES(@a,@b,@c,@d,@e)", "@a", v.No, "@b", i.C, "@c", i.N, "@d", i.Pr, "@e", i.Q);
                            }
                            Exec(c, t, "IF EXISTS(SELECT 1 FROM Config WHERE Clave='Biz') UPDATE Config SET Valor=@v WHERE Clave='Biz' ELSE INSERT INTO Config(Clave,Valor) VALUES('Biz',@v)", "@v", D.Biz);
                            t.Commit();
                        }
                        catch { t.Rollback(); throw; }
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show("No se pudo guardar en SQL Server:\n" + ex.Message, "Error de base de datos"); }
        }

        public static string Hash(string p)
        {
            using (var s = SHA256.Create())
                return BitConverter.ToString(s.ComputeHash(Encoding.UTF8.GetBytes("pos|" + p))).Replace("-", "");
        }
        public static string M(decimal n) { return n.ToString("N2", CultureInfo.InvariantCulture); }
        public static decimal Num(string s)
        {
            decimal v;
            if (decimal.TryParse((s ?? "").Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }
    }

    // ---------- Recibo TXT / PDF ----------
    static class Printer
    {
        public static List<string> Receipt(Invoice v)
        {
            int W = 44; string ln = new string('-', W);
            Func<string, string> c = delegate (string s) { return new string(' ', Math.Max(0, (W - s.Length) / 2)) + s; };
            Func<string, string, string> r = delegate (string a, string b) { return a + new string(' ', Math.Max(1, W - a.Length - b.Length)) + b; };
            var L = new List<string>();
            L.Add(c(Store.D.Biz)); L.Add(c("FACTURA")); L.Add(ln);
            L.Add("No.:     " + v.No.ToString("D6"));
            L.Add("Fecha:   " + v.D.ToString("dd/MM/yyyy hh:mm tt", CultureInfo.InvariantCulture));
            L.Add("Cajero:  " + v.U); L.Add("Cliente: " + v.Cn);
            if (!string.IsNullOrEmpty(v.Cr)) L.Add("RNC/Ced: " + v.Cr);
            L.Add(ln); L.Add(r("Artículo", "Importe"));
            foreach (var i in v.Items)
            {
                L.Add(i.N.Length > W ? i.N.Substring(0, W) : i.N);
                L.Add(r("  " + i.Q + " x " + Store.M(i.Pr), Store.M(i.Pr * i.Q)));
            }
            L.Add(ln); L.Add(r("Subtotal", Store.M(v.Sub)));
            if (v.Dc > 0) L.Add(r("Descuento", "-" + Store.M(v.Dc)));
            L.Add(r("ITBIS 18%", Store.M(v.Itb))); L.Add(r("TOTAL RD$", Store.M(v.Total))); L.Add(ln);
            L.Add("Pago: " + v.Pm);
            if (v.Pm == "Efectivo") { L.Add(r("Recibido", Store.M(v.Rv))); L.Add(r("Devuelta", Store.M(Math.Max(0, v.Rv - v.Total)))); }
            L.Add(ln); L.Add(c("¡Gracias por su compra!"));
            return L;
        }

        static string Esc(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s)
            {
                char x = (ch < 32 || ch > 255) ? '?' : ch;
                if (x == '\\' || x == '(' || x == ')') sb.Append('\\');
                sb.Append(x);
            }
            return sb.ToString();
        }

        public static byte[] MakePdf(List<string> lines)
        {
            int pp = 58; var pages = new List<List<string>>();
            for (int i = 0; i < lines.Count; i += pp) pages.Add(lines.GetRange(i, Math.Min(pp, lines.Count - i)));
            if (pages.Count == 0) pages.Add(new List<string>());
            var o = new Dictionary<int, string>();
            var kids = new StringBuilder();
            for (int i = 0; i < pages.Count; i++) kids.Append((4 + i * 2) + " 0 R ");
            o[1] = "<</Type/Catalog/Pages 2 0 R>>";
            o[2] = "<</Type/Pages/Kids[" + kids + "]/Count " + pages.Count + ">>";
            o[3] = "<</Type/Font/Subtype/Type1/BaseFont/Courier/Encoding/WinAnsiEncoding>>";
            for (int i = 0; i < pages.Count; i++)
            {
                var sb = new StringBuilder("BT /F1 10 Tf 12 TL 50 750 Td ");
                foreach (var l in pages[i]) sb.Append("(" + Esc(l) + ") Tj T* ");
                sb.Append("ET"); string s = sb.ToString();
                o[4 + i * 2] = "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Resources<</Font<</F1 3 0 R>>>>/Contents " + (5 + i * 2) + " 0 R>>";
                o[5 + i * 2] = "<</Length " + s.Length + ">>\nstream\n" + s + "\nendstream";
            }
            var outp = new StringBuilder("%PDF-1.4\n"); var off = new Dictionary<int, int>();
            for (int k = 1; k <= o.Count; k++) { off[k] = outp.Length; outp.Append(k + " 0 obj\n" + o[k] + "\nendobj\n"); }
            int x0 = outp.Length;
            outp.Append("xref\n0 " + (o.Count + 1) + "\n0000000000 65535 f \n");
            for (int k = 1; k <= o.Count; k++) outp.Append(off[k].ToString("D10") + " 00000 n \n");
            outp.Append("trailer\n<</Size " + (o.Count + 1) + "/Root 1 0 R>>\nstartxref\n" + x0 + "\n%%EOF");
            return Encoding.GetEncoding("iso-8859-1").GetBytes(outp.ToString());
        }

        public static void Export(Invoice v, string kind, IWin32Window owner)
        {
            var d = new SaveFileDialog();
            d.FileName = "Factura-" + v.No.ToString("D6");
            d.Filter = kind == "pdf" ? "PDF (*.pdf)|*.pdf" : "Texto (*.txt)|*.txt";
            d.DefaultExt = kind;
            if (d.ShowDialog(owner) != DialogResult.OK) return;
            var L = Receipt(v);
            if (kind == "txt") File.WriteAllText(d.FileName, string.Join("\r\n", L.ToArray()), new UTF8Encoding(true));
            else File.WriteAllBytes(d.FileName, MakePdf(L));
            try { Process.Start(d.FileName); } catch { MessageBox.Show("Archivo guardado en:\n" + d.FileName); }
        }
    }

    // ---------- Utilidades de interfaz ----------
    static class UI
    {
        public static Label Lbl(Control p, string t, int x, int y)
        { var l = new Label(); l.Text = t; l.Left = x; l.Top = y; l.AutoSize = true; p.Controls.Add(l); return l; }
        public static TextBox Txt(Control p, int x, int y, int w)
        { var t = new TextBox(); t.Left = x; t.Top = y; t.Width = w; p.Controls.Add(t); return t; }
        public static Button Btn(Control p, string t, int x, int y, int w, EventHandler h)
        { var b = new Button(); b.Text = t; b.Left = x; b.Top = y; b.Width = w; b.Height = 30; b.Click += h; p.Controls.Add(b); return b; }
        public static ComboBox Combo(Control p, int x, int y, int w, params string[] items)
        { var c = new ComboBox(); c.DropDownStyle = ComboBoxStyle.DropDownList; c.Left = x; c.Top = y; c.Width = w; c.Items.AddRange(items); c.SelectedIndex = 0; p.Controls.Add(c); return c; }
        public static ListView LV(params string[] cols)
        {
            var v = new ListView(); v.View = View.Details; v.FullRowSelect = true; v.GridLines = true; v.HideSelection = false; v.MultiSelect = false;
            foreach (var c in cols)
            {
                var p = c.Split(':'); var h = new ColumnHeader(); h.Text = p[0]; h.Width = int.Parse(p[1]);
                if (p.Length > 2) h.TextAlign = HorizontalAlignment.Right; v.Columns.Add(h);
            }
            return v;
        }
        public static string Prompt(string title, string text, string def, bool pw)
        {
            var f = new Form(); f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.StartPosition = FormStartPosition.CenterParent;
            f.MaximizeBox = false; f.MinimizeBox = false; f.ClientSize = new Size(340, 124);
            var l = new Label(); l.Text = text; l.Left = 12; l.Top = 10; l.Width = 316; l.Height = 36;
            var t = new TextBox(); t.Left = 12; t.Top = 50; t.Width = 316; t.Text = def ?? ""; t.UseSystemPasswordChar = pw;
            var ok = new Button(); ok.Text = "Aceptar"; ok.Left = 160; ok.Top = 84; ok.Width = 80; ok.DialogResult = DialogResult.OK;
            var c = new Button(); c.Text = "Cancelar"; c.Left = 248; c.Top = 84; c.Width = 80; c.DialogResult = DialogResult.Cancel;
            f.Controls.Add(l); f.Controls.Add(t); f.Controls.Add(ok); f.Controls.Add(c); f.AcceptButton = ok; f.CancelButton = c;
            return f.ShowDialog() == DialogResult.OK ? t.Text : null;
        }
    }

    // ---------- Login ----------
    class LoginForm : Form
    {
        public User Result; TextBox tu, tp;
        public LoginForm()
        {
            Text = "Punto de Venta - Iniciar sesión"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(330, 270); Font = new Font("Segoe UI", 10f);
            var h = UI.Lbl(this, Store.D.Biz, 20, 18); h.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            UI.Lbl(this, "Inicie sesión para continuar", 20, 54);
            UI.Lbl(this, "Usuario", 20, 88); tu = UI.Txt(this, 20, 110, 290);
            UI.Lbl(this, "Contraseña", 20, 144); tp = UI.Txt(this, 20, 166, 290); tp.UseSystemPasswordChar = true;
            var b = UI.Btn(this, "Entrar", 20, 204, 290, DoLogin); b.Height = 34; AcceptButton = b;
            var n = UI.Lbl(this, "Primer acceso: admin / admin123", 20, 244); n.ForeColor = Color.Gray;
        }
        void DoLogin(object s, EventArgs e)
        {
            string u = tu.Text.Trim().ToLower(), p = Store.Hash(tp.Text);
            foreach (var x in Store.D.Users)
                if (x.U == u && x.P == p) { Result = x; DialogResult = DialogResult.OK; Close(); return; }
            MessageBox.Show("Usuario o contraseña incorrectos.", "Acceso denegado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------- Ventana principal ----------
    class MainForm : Form
    {
        public bool Relogin;
        User me; List<Item> cart = new List<Item>(); Invoice last;
        TabControl tabs; TabPage tFact, tHist, tProd, tUsr;
        TextBox q, cn, cr, dc, rv; ComboBox pm; ListBox sug; ListView lv; Label lsub, litb, lchg, ltot;
        List<Product> sugItems = new List<Product>();
        ListView hv; Label ltoday;
        ListView pv; TextBox pc, pn, ppr, pst;
        ListView uv; TextBox un, uu, up; ComboBox ur; TextBox bz;
        TabPage tRep; DateTimePicker dtD, dtH; ListView rep; List<string> repLines = new List<string>();

        public MainForm(User u)
        {
            me = u; Text = "Punto de Venta"; ClientSize = new Size(1060, 660); MinimumSize = new Size(960, 600);
            StartPosition = FormStartPosition.CenterScreen; KeyPreview = true; Font = new Font("Segoe UI", 10f);

            var top = new Panel(); top.Dock = DockStyle.Top; top.Height = 44; top.BackColor = Color.FromArgb(22, 32, 46);
            var lb = new Label(); lb.Text = Store.D.Biz; lb.Dock = DockStyle.Left; lb.Width = 320; lb.ForeColor = Color.White;
            lb.Font = new Font("Segoe UI", 13f, FontStyle.Bold); lb.TextAlign = ContentAlignment.MiddleLeft; lb.Padding = new Padding(10, 0, 0, 0);
            var lu = new Label(); lu.Text = me.N + (me.R == "admin" ? " (administrador)" : " (cajero)"); lu.Dock = DockStyle.Right; lu.Width = 260;
            lu.ForeColor = Color.White; lu.TextAlign = ContentAlignment.MiddleRight;
            var bo = new Button(); bo.Text = "Salir"; bo.Dock = DockStyle.Right; bo.Width = 90; bo.FlatStyle = FlatStyle.Flat; bo.ForeColor = Color.White;
            bo.Click += delegate { Relogin = true; Close(); };
            top.Controls.Add(lb); top.Controls.Add(lu); top.Controls.Add(bo);

            tabs = new TabControl(); tabs.Dock = DockStyle.Fill;
            tFact = new TabPage("Facturar"); tHist = new TabPage("Facturas"); tabs.TabPages.Add(tFact); tabs.TabPages.Add(tHist);
            BuildFact(tFact); BuildHist(tHist);
            if (me.R == "admin")
            {
                tProd = new TabPage("Productos"); tUsr = new TabPage("Usuarios"); tRep = new TabPage("Reportes"); tabs.TabPages.Add(tProd); tabs.TabPages.Add(tUsr); tabs.TabPages.Add(tRep);
                BuildProd(tProd); BuildUsr(tUsr); BuildRep(tRep);
            }
            tabs.SelectedIndexChanged += delegate
            {
                if (tabs.SelectedTab == tHist) FillHist(); else if (tabs.SelectedTab == tProd) FillProd(); else if (tabs.SelectedTab == tFact) q.Focus();
            };
            Controls.Add(tabs); Controls.Add(top);
            Shown += delegate { q.Focus(); };
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5) { e.Handled = true; e.SuppressKeyPress = true; F5(); return; }
            base.OnKeyDown(e);
        }

        void F5()
        {
            if (tabs.SelectedTab == tFact)
            {
                if (cart.Count > 0) Cobrar();
                else if (last != null) AskPrint(last);
                else MessageBox.Show("No hay factura para imprimir.");
            }
            else if (tabs.SelectedTab == tHist)
            {
                if (hv.SelectedItems.Count == 0) { MessageBox.Show("Seleccione una factura de la lista."); return; }
                AskPrint((Invoice)hv.SelectedItems[0].Tag);
            }
        }

        // ----- Facturar -----
        void BuildFact(TabPage t)
        {
            var left = new Panel(); left.Dock = DockStyle.Fill;
            var right = new Panel(); right.Dock = DockStyle.Right; right.Width = 330; right.Padding = new Padding(10);
            t.Controls.Add(left); t.Controls.Add(right);

            lv = UI.LV("Artículo:300", "Precio:90:r", "Cant.:60:r", "Importe:100:r"); lv.Dock = DockStyle.Fill;
            lv.DoubleClick += delegate { SetQty(); };
            var bot = new Panel(); bot.Dock = DockStyle.Bottom; bot.Height = 46;
            UI.Btn(bot, "Cambiar cantidad", 10, 8, 140, delegate { SetQty(); });
            UI.Btn(bot, "Quitar", 160, 8, 90, delegate { if (lv.SelectedIndices.Count > 0) { cart.RemoveAt(lv.SelectedIndices[0]); Draw(); } });
            UI.Btn(bot, "Limpiar", 260, 8, 90, delegate { ResetSale(); });
            var topP = new Panel(); topP.Dock = DockStyle.Top; topP.Height = 140;
            sug = new ListBox(); sug.Dock = DockStyle.Top; sug.Height = 80; sug.DoubleClick += delegate { Pick(); };
            q = new TextBox(); q.Dock = DockStyle.Top; q.Font = new Font("Segoe UI", 13f);
            var ql = new Label(); ql.Text = "Código o nombre del artículo (Enter para agregar)"; ql.Dock = DockStyle.Top; ql.Height = 24; ql.Padding = new Padding(2, 4, 0, 0);
            topP.Controls.Add(sug); topP.Controls.Add(q); topP.Controls.Add(ql);
            q.TextChanged += delegate { Suggest(); };
            q.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; e.Handled = true; AddEnter(); } };
            left.Controls.Add(lv); left.Controls.Add(bot); left.Controls.Add(topP);

            UI.Lbl(right, "Cliente (vacío = Consumidor final)", 10, 8); cn = UI.Txt(right, 10, 30, 300);
            UI.Lbl(right, "RNC / Cédula (opcional)", 10, 66); cr = UI.Txt(right, 10, 88, 300);
            UI.Lbl(right, "Forma de pago", 10, 124); pm = UI.Combo(right, 10, 146, 140, "Efectivo", "Tarjeta", "Transferencia");
            UI.Lbl(right, "Descuento RD$", 170, 124); dc = UI.Txt(right, 170, 146, 140);
            UI.Lbl(right, "Monto recibido", 10, 184); rv = UI.Txt(right, 10, 206, 300);
            EventHandler rc = delegate { Calc(); }; dc.TextChanged += rc; rv.TextChanged += rc; pm.SelectedIndexChanged += rc;
            lsub = Val(right, "Subtotal", 252); litb = Val(right, "ITBIS 18%", 280); lchg = Val(right, "Devuelta", 308);
            var tl = UI.Lbl(right, "TOTAL", 10, 346); tl.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            ltot = new Label(); ltot.Left = 100; ltot.Top = 342; ltot.Width = 210; ltot.Height = 34; ltot.TextAlign = ContentAlignment.MiddleRight;
            ltot.Font = new Font("Segoe UI", 16f, FontStyle.Bold); ltot.ForeColor = Color.FromArgb(15, 92, 99); right.Controls.Add(ltot);
            var bc = UI.Btn(right, "Cobrar e imprimir (F5)", 10, 396, 300, delegate { Cobrar(); }); bc.Height = 44;
            Calc();
        }

        Label Val(Control p, string text, int y)
        {
            UI.Lbl(p, text, 10, y);
            var l = new Label(); l.Left = 160; l.Top = y; l.Width = 150; l.Height = 22; l.TextAlign = ContentAlignment.MiddleRight; l.Text = "0.00"; p.Controls.Add(l); return l;
        }

        void Suggest()
        {
            sug.Items.Clear(); sugItems.Clear(); string v = q.Text.Trim().ToLower(); if (v.Length == 0) return;
            foreach (var p in Store.D.Products)
            {
                if (p.C.ToLower().Contains(v) || p.N.ToLower().Contains(v))
                {
                    sugItems.Add(p); sug.Items.Add(p.C + "  " + p.N + "  -  " + Store.M(p.Pr) + "  (exist. " + p.St + ")");
                    if (sugItems.Count >= 8) break;
                }
            }
        }
        void Pick() { if (sug.SelectedIndex >= 0) { Add(sugItems[sug.SelectedIndex].C); q.Text = ""; } }
        void AddEnter()
        {
            string v = q.Text.Trim().ToLower(); if (v.Length == 0) return;
            Product f = null;
            foreach (var p in Store.D.Products) if (p.C.ToLower() == v) { f = p; break; }
            if (f == null && sugItems.Count > 0) f = sugItems[0];
            if (f == null) { MessageBox.Show("Artículo no encontrado."); return; }
            Add(f.C); q.Text = "";
        }
        Product Find(string c) { foreach (var p in Store.D.Products) if (p.C == c) return p; return null; }
        void Add(string c)
        {
            var p = Find(c); if (p == null) return;
            Item l = null; foreach (var x in cart) if (x.C == c) { l = x; break; }
            int qn = (l != null ? l.Q : 0) + 1;
            if (qn > p.St) { MessageBox.Show("Existencia insuficiente de " + p.N + " (" + p.St + ").", "Sin existencia"); return; }
            if (l != null) l.Q = qn; else cart.Add(new Item { C = p.C, N = p.N, Pr = p.Pr, Q = 1 });
            Draw();
        }
        void SetQty()
        {
            if (lv.SelectedIndices.Count == 0) return;
            int i = lv.SelectedIndices[0]; var l = cart[i]; var p = Find(l.C); int st = p != null ? p.St : 0;
            string r = UI.Prompt("Cantidad", l.N + " (disponible: " + st + ")", l.Q.ToString(), false); if (r == null) return;
            int n; if (!int.TryParse(r.Trim(), out n) || n < 0) return;
            if (n > st) { n = st; MessageBox.Show("Máximo disponible: " + st); }
            if (n == 0) cart.RemoveAt(i); else l.Q = n;
            Draw();
        }
        void ResetSale() { cart.Clear(); cn.Text = ""; cr.Text = ""; dc.Text = ""; rv.Text = ""; pm.SelectedIndex = 0; Draw(); q.Focus(); }
        void Draw()
        {
            lv.Items.Clear();
            foreach (var l in cart) lv.Items.Add(new ListViewItem(new string[] { l.N, Store.M(l.Pr), l.Q.ToString(), Store.M(l.Pr * l.Q) }));
            Calc();
        }
        void Totals(out decimal sub, out decimal d, out decimal itb, out decimal tot)
        {
            sub = 0; foreach (var l in cart) sub += l.Pr * l.Q;
            d = Math.Min(Math.Max(0, Store.Num(dc.Text)), sub);
            itb = Math.Round((sub - d) * Store.ITBIS, 2); tot = sub - d + itb;
        }
        void Calc()
        {
            decimal sub, d, itb, tot; Totals(out sub, out d, out itb, out tot); decimal r = Store.Num(rv.Text);
            lsub.Text = Store.M(sub); litb.Text = Store.M(itb); ltot.Text = "RD$ " + Store.M(tot);
            lchg.Text = (pm.Text == "Efectivo" && r > tot) ? Store.M(r - tot) : "0.00";
        }
        void Cobrar()
        {
            if (cart.Count == 0) { MessageBox.Show("Agregue artículos a la factura."); return; }
            decimal sub, d, itb, tot; Totals(out sub, out d, out itb, out tot);
            string pago = pm.Text; bool hasRv = rv.Text.Trim().Length > 0; decimal r = Store.Num(rv.Text);
            if (pago == "Efectivo" && hasRv && r < tot) { MessageBox.Show("El monto recibido es menor al total."); return; }
            foreach (var l in cart) { var p = Find(l.C); if (p == null || p.St < l.Q) { MessageBox.Show("Existencia insuficiente: " + l.N); return; } }
            var inv = new Invoice();
            inv.No = Store.D.Seq++; inv.D = DateTime.Now; inv.U = me.N;
            inv.Cn = cn.Text.Trim().Length > 0 ? cn.Text.Trim() : "Consumidor final"; inv.Cr = cr.Text.Trim();
            inv.Sub = sub; inv.Dc = d; inv.Itb = itb; inv.Total = tot; inv.Pm = pago; inv.Rv = (pago == "Efectivo" && hasRv) ? r : tot;
            foreach (var l in cart) { inv.Items.Add(new Item { C = l.C, N = l.N, Pr = l.Pr, Q = l.Q }); Find(l.C).St -= l.Q; }
            Store.D.Invoices.Insert(0, inv); Store.Save();
            ResetSale(); AskPrint(inv);
        }

        // ----- Impresión -----
        void AskPrint(Invoice v)
        {
            last = v;
            var f = new Form(); f.Text = "Imprimir factura"; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.StartPosition = FormStartPosition.CenterParent;
            f.MaximizeBox = false; f.MinimizeBox = false; f.ClientSize = new Size(360, 150); f.Font = Font;
            var t = UI.Lbl(f, "Factura " + v.No.ToString("D6"), 20, 14); t.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            UI.Lbl(f, "Total: RD$ " + Store.M(v.Total) + "\nElija el formato de impresión:", 20, 48);
            UI.Btn(f, "Guardar PDF", 20, 104, 105, delegate { f.Close(); Printer.Export(v, "pdf", this); });
            UI.Btn(f, "Guardar TXT", 135, 104, 105, delegate { f.Close(); Printer.Export(v, "txt", this); });
            var c = UI.Btn(f, "Cerrar", 250, 104, 90, delegate { f.Close(); }); f.CancelButton = c;
            f.ShowDialog(this);
        }

        // ----- Facturas -----
        void BuildHist(TabPage t)
        {
            hv = UI.LV("No.:70:r", "Fecha:160", "Cliente:240", "Cajero:150", "Pago:110", "Total:110:r"); hv.Dock = DockStyle.Fill;
            hv.DoubleClick += delegate { F5(); };
            var top = new Panel(); top.Dock = DockStyle.Top; top.Height = 40;
            UI.Lbl(top, "Seleccione una factura y presione F5 para imprimir (o doble clic).", 10, 10);
            ltoday = new Label(); ltoday.Dock = DockStyle.Right; ltoday.Width = 330; ltoday.TextAlign = ContentAlignment.MiddleRight;
            ltoday.Font = new Font("Segoe UI", 11f, FontStyle.Bold); top.Controls.Add(ltoday);
            t.Controls.Add(hv); t.Controls.Add(top);
        }
        void FillHist()
        {
            hv.Items.Clear(); decimal hoy = 0;
            foreach (var i in Store.D.Invoices)
            {
                if (me.R != "admin" && i.U != me.N) continue;
                var it = new ListViewItem(new string[] { i.No.ToString("D6"), i.D.ToString("dd/MM/yyyy HH:mm"), i.Cn, i.U, i.Pm, Store.M(i.Total) });
                it.Tag = i; hv.Items.Add(it);
                if (i.D.Date == DateTime.Today) hoy += i.Total;
            }
            ltoday.Text = "Ventas de hoy: RD$ " + Store.M(hoy);
        }


        // ----- Reportes por cajero (admin) -----
        void BuildRep(TabPage t)
        {
            var top = new Panel(); top.Dock = DockStyle.Top; top.Height = 80;
            UI.Lbl(top, "Desde", 10, 8); dtD = new DateTimePicker(); dtD.Format = DateTimePickerFormat.Short; dtD.Left = 10; dtD.Top = 30; dtD.Width = 130; dtD.Value = DateTime.Today; top.Controls.Add(dtD);
            UI.Lbl(top, "Hasta", 160, 8); dtH = new DateTimePicker(); dtH.Format = DateTimePickerFormat.Short; dtH.Left = 160; dtH.Top = 30; dtH.Width = 130; dtH.Value = DateTime.Today; top.Controls.Add(dtH);
            UI.Btn(top, "Generar reporte", 310, 28, 140, delegate { GenRep(); });
            UI.Btn(top, "Guardar PDF", 460, 28, 120, delegate { SaveRep("pdf"); });
            UI.Btn(top, "Guardar TXT", 590, 28, 120, delegate { SaveRep("txt"); });
            rep = UI.LV("Cajero:240", "Facturas:90:r", "Subtotal:130:r", "Descuento:120:r", "ITBIS:120:r", "Total:140:r"); rep.Dock = DockStyle.Fill;
            t.Controls.Add(rep); t.Controls.Add(top);
        }
        void GenRep()
        {
            DateTime d1 = dtD.Value.Date, d2 = dtH.Value.Date.AddDays(1);
            var rows = new Dictionary<string, decimal[]>(); var order = new List<string>(); decimal[] all = new decimal[5];
            foreach (var i in Store.D.Invoices)
            {
                if (i.D < d1 || i.D >= d2) continue;
                if (!rows.ContainsKey(i.U)) { rows[i.U] = new decimal[5]; order.Add(i.U); }
                decimal[] a = rows[i.U], v = new decimal[] { 1, i.Sub, i.Dc, i.Itb, i.Total };
                for (int k = 0; k < 5; k++) { a[k] += v[k]; all[k] += v[k]; }
            }
            order.Sort();
            rep.Items.Clear(); repLines = new List<string>();
            string ln = new string('-', 70);
            repLines.Add(Store.D.Biz); repLines.Add("REPORTE DE VENTAS POR CAJERO");
            repLines.Add("Desde: " + d1.ToString("dd/MM/yyyy") + "   Hasta: " + dtH.Value.Date.ToString("dd/MM/yyyy"));
            repLines.Add("Generado: " + DateTime.Now.ToString("dd/MM/yyyy hh:mm tt", CultureInfo.InvariantCulture));
            repLines.Add(ln);
            repLines.Add(Row("Cajero", "Fact.", "Subtotal", "Desc.", "ITBIS", "Total"));
            repLines.Add(ln);
            foreach (var n in order)
            {
                decimal[] a = rows[n];
                rep.Items.Add(new ListViewItem(new string[] { n, a[0].ToString("0"), Store.M(a[1]), Store.M(a[2]), Store.M(a[3]), Store.M(a[4]) }));
                repLines.Add(Row(n, a[0].ToString("0"), Store.M(a[1]), Store.M(a[2]), Store.M(a[3]), Store.M(a[4])));
            }
            var tot = new ListViewItem(new string[] { "TOTAL GENERAL", all[0].ToString("0"), Store.M(all[1]), Store.M(all[2]), Store.M(all[3]), Store.M(all[4]) });
            tot.Font = new Font(Font, FontStyle.Bold); rep.Items.Add(tot);
            repLines.Add(ln); repLines.Add(Row("TOTAL GENERAL", all[0].ToString("0"), Store.M(all[1]), Store.M(all[2]), Store.M(all[3]), Store.M(all[4])));
            if (order.Count == 0) MessageBox.Show("No hay facturas en ese rango de fechas.");
        }
        static string Row(string n, string c, string s, string d, string i, string t)
        {
            if (n.Length > 16) n = n.Substring(0, 16);
            return n.PadRight(16) + c.PadLeft(6) + s.PadLeft(12) + d.PadLeft(10) + i.PadLeft(12) + t.PadLeft(14);
        }
        void SaveRep(string kind)
        {
            GenRep(); if (repLines.Count == 0) return;
            var d = new SaveFileDialog(); d.FileName = "Reporte-cajeros-" + DateTime.Now.ToString("yyyyMMdd");
            d.Filter = kind == "pdf" ? "PDF (*.pdf)|*.pdf" : "Texto (*.txt)|*.txt"; d.DefaultExt = kind;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (kind == "txt") File.WriteAllText(d.FileName, string.Join("\r\n", repLines.ToArray()), new UTF8Encoding(true));
            else File.WriteAllBytes(d.FileName, Printer.MakePdf(repLines));
            try { Process.Start(d.FileName); } catch { MessageBox.Show("Archivo guardado en:\n" + d.FileName); }
        }

        // ----- Productos (admin) -----
        void BuildProd(TabPage t)
        {
            var top = new Panel(); top.Dock = DockStyle.Top; top.Height = 96;
            UI.Lbl(top, "Código", 10, 8); pc = UI.Txt(top, 10, 30, 100);
            UI.Lbl(top, "Nombre", 120, 8); pn = UI.Txt(top, 120, 30, 300);
            UI.Lbl(top, "Precio (sin ITBIS)", 430, 8); ppr = UI.Txt(top, 430, 30, 120);
            UI.Lbl(top, "Existencia", 560, 8); pst = UI.Txt(top, 560, 30, 90);
            UI.Btn(top, "Guardar", 665, 28, 90, delegate { SaveProd(); });
            UI.Btn(top, "Borrar", 765, 28, 90, delegate { DelProd(); });
            var h = UI.Lbl(top, "Si el código ya existe, se actualiza el artículo.", 10, 66); h.ForeColor = Color.Gray;
            pv = UI.LV("Código:100", "Nombre:340", "Precio:110:r", "Existencia:110:r"); pv.Dock = DockStyle.Fill;
            pv.SelectedIndexChanged += delegate
            {
                if (pv.SelectedItems.Count == 0) return; var p = (Product)pv.SelectedItems[0].Tag;
                pc.Text = p.C; pn.Text = p.N; ppr.Text = p.Pr.ToString(CultureInfo.InvariantCulture); pst.Text = p.St.ToString();
            };
            t.Controls.Add(pv); t.Controls.Add(top);
        }
        void FillProd()
        {
            pv.Items.Clear();
            foreach (var p in Store.D.Products) { var it = new ListViewItem(new string[] { p.C, p.N, Store.M(p.Pr), p.St.ToString() }); it.Tag = p; pv.Items.Add(it); }
        }
        void SaveProd()
        {
            string c = pc.Text.Trim(), n = pn.Text.Trim(); decimal pr = Store.Num(ppr.Text); int st = (int)Store.Num(pst.Text);
            if (c.Length == 0 || n.Length == 0 || ppr.Text.Trim().Length == 0 || pr < 0) { MessageBox.Show("Complete código, nombre y precio."); return; }
            var p = Find(c); if (p == null) { p = new Product(); p.C = c; Store.D.Products.Add(p); }
            p.N = n; p.Pr = pr; p.St = st; Store.Save(); FillProd();
            pc.Text = ""; pn.Text = ""; ppr.Text = ""; pst.Text = "";
        }
        void DelProd()
        {
            var p = Find(pc.Text.Trim()); if (p == null) return;
            if (MessageBox.Show("¿Borrar el artículo " + p.N + "?", "Borrar", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Store.D.Products.Remove(p); Store.Save(); FillProd(); pc.Text = ""; pn.Text = ""; ppr.Text = ""; pst.Text = "";
        }

        // ----- Usuarios (admin) -----
        void BuildUsr(TabPage t)
        {
            var top = new Panel(); top.Dock = DockStyle.Top; top.Height = 76;
            UI.Lbl(top, "Nombre", 10, 8); un = UI.Txt(top, 10, 30, 230);
            UI.Lbl(top, "Usuario", 250, 8); uu = UI.Txt(top, 250, 30, 150);
            UI.Lbl(top, "Contraseña", 410, 8); up = UI.Txt(top, 410, 30, 150); up.UseSystemPasswordChar = true;
            UI.Lbl(top, "Rol", 570, 8); ur = UI.Combo(top, 570, 30, 110, "cajero", "admin");
            UI.Btn(top, "Crear usuario", 695, 28, 120, delegate { AddUsr(); });
            var bot = new Panel(); bot.Dock = DockStyle.Bottom; bot.Height = 150;
            UI.Btn(bot, "Cambiar contraseña", 10, 8, 160, delegate { Pwd(); });
            UI.Btn(bot, "Borrar usuario", 180, 8, 130, delegate { DelUsr(); });
            UI.Lbl(bot, "Nombre del negocio (sale en las facturas)", 10, 60); bz = UI.Txt(bot, 10, 84, 300); bz.Text = Store.D.Biz;
            UI.Btn(bot, "Guardar", 320, 82, 90, delegate
            {
                if (bz.Text.Trim().Length > 0) { Store.D.Biz = bz.Text.Trim(); Store.Save(); MessageBox.Show("Guardado. Se verá en el encabezado al volver a iniciar sesión."); }
            });
            uv = UI.LV("Usuario:160", "Nombre:280", "Rol:110"); uv.Dock = DockStyle.Fill;
            t.Controls.Add(uv); t.Controls.Add(bot); t.Controls.Add(top);
            FillUsr();
        }
        void FillUsr()
        {
            uv.Items.Clear();
            foreach (var u in Store.D.Users) { var it = new ListViewItem(new string[] { u.U, u.N, u.R }); it.Tag = u; uv.Items.Add(it); }
        }
        void AddUsr()
        {
            string n = un.Text.Trim(), u = uu.Text.Trim().ToLower(), p = up.Text;
            if (n.Length == 0 || u.Length == 0 || p.Length < 4) { MessageBox.Show("Complete los datos (contraseña de 4 o más caracteres)."); return; }
            foreach (var x in Store.D.Users) if (x.U == u) { MessageBox.Show("Ese usuario ya existe."); return; }
            Store.D.Users.Add(new User { U = u, N = n, P = Store.Hash(p), R = ur.Text }); Store.Save(); FillUsr();
            un.Text = ""; uu.Text = ""; up.Text = "";
        }
        User SelUsr() { return uv.SelectedItems.Count > 0 ? (User)uv.SelectedItems[0].Tag : null; }
        void Pwd()
        {
            var u = SelUsr(); if (u == null) { MessageBox.Show("Seleccione un usuario."); return; }
            string p = UI.Prompt("Contraseña", "Nueva contraseña para " + u.U + " (mínimo 4 caracteres):", "", true);
            if (p != null && p.Length >= 4) { u.P = Store.Hash(p); Store.Save(); MessageBox.Show("Contraseña actualizada."); }
        }
        void DelUsr()
        {
            var u = SelUsr(); if (u == null) return;
            if (u.U == me.U) { MessageBox.Show("No puede borrar su propio usuario."); return; }
            if (MessageBox.Show("¿Borrar al usuario " + u.U + "?", "Borrar", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Store.D.Users.Remove(u); Store.Save(); FillUsr();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Store.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo conectar a SQL Server:\n" + ex.Message + "\n\nRevise el archivo conexion.txt (junto al .exe) y que la base de datos PuntoDeVenta exista.", "Error de conexión");
                return;
            }
            while (true)
            {
                var lf = new LoginForm();
                if (lf.ShowDialog() != DialogResult.OK) break;
                var mf = new MainForm(lf.Result);
                Application.Run(mf);
                if (!mf.Relogin) break;
            }
        }
    }
}
