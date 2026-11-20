// The docked findings panel (a borderless form Notepad++ docks at the bottom) and the Main decks dialog.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Deckcheck.Npp
{
    public sealed class Panel : Form
    {
        readonly Label _summary = new Label();
        readonly ListView _list = new ListView();
        // One list, one scroll bar at any panel height (owner trial: a separate notes box was too short to scroll).
        // Rows: unsaved-change notes, then the findings, then the "Not checked" notes, in grey.
        List<Finding> _rows = new List<Finding>();
        List<string> _above = new List<string>(), _below = new List<string>();
        public event Action<Finding> JumpTo;

        public Panel()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Text = "deckcheck";
            _summary.Dock = DockStyle.Top;
            _summary.AutoSize = false;
            _summary.Height = 22;
            _summary.TextAlign = ContentAlignment.MiddleLeft;
            _summary.Padding = new Padding(4, 0, 0, 0);
            _summary.Text = "deckcheck: Plugins > deckcheck > Check model (Alt+Shift+K), or save a deck.";
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.Columns.Add("Severity", 70);
            _list.Columns.Add("Code", 60);
            _list.Columns.Add("Location", 180);
            _list.Columns.Add("Message", 700);
            _list.ItemActivate += delegate { Activate_(); };
            _list.VirtualMode = true;  // rows are built only when shown: a model can have tens of thousands (spec §5)
            _list.VirtualListSize = 0;
            _list.RetrieveVirtualItem += (s, e) => { e.Item = ItemAt(e.ItemIndex); };
            Controls.Add(_list);
            Controls.Add(_summary);
        }

        ListViewItem ItemAt(int i)
        {
            if (i < _above.Count) return Note(_above[i], Color.DarkGoldenrod);
            i -= _above.Count;
            if (i < _rows.Count)
            {
                Finding f = _rows[i];
                ListViewItem it = new ListViewItem(PanelText.Row(f));
                it.ForeColor = f.Severity == "error" ? Color.Firebrick : Color.DarkGoldenrod;
                return it;
            }
            return Note(_below[i - _rows.Count], Color.DimGray);
        }

        static ListViewItem Note(string text, Color color)
        {
            ListViewItem it = new ListViewItem(new[] { "", "", "", text });
            it.ForeColor = color;
            return it;
        }

        void Activate_()
        {
            if (_list.SelectedIndices.Count != 1 || JumpTo == null) return;
            int i = _list.SelectedIndices[0] - _above.Count;
            if (i >= 0 && i < _rows.Count) JumpTo(_rows[i]);  // note rows do nothing
        }

        void SetRows(List<Finding> rows, List<string> above, List<string> below)
        {
            _rows = rows;
            _above = above;
            _below = below;
            _list.VirtualListSize = _above.Count + _rows.Count + _below.Count;
            _list.Invalidate();
        }

        public void ShowRunning(string file)
        {
            _summary.Text = "Checking the model of " + Path.GetFileName(file) + "...";
            SetRows(new List<Finding>(), new List<string>(), new List<string>());
        }

        public void ShowMessage(string message)
        {
            _summary.Text = message;
            SetRows(new List<Finding>(), new List<string>(), new List<string>());
        }

        public void ShowResult(CheckResult r, double seconds, string file, IEnumerable<string> unsaved)
        {
            _summary.Text = PanelText.Summary(r, seconds, file);
            List<string> above, below;
            PanelText.NoteRows(r, unsaved, out above, out below);
            SetRows(r.Diagnostics, above, below);
        }

        public void MarkEdited()
        {
            if (!_summary.Text.EndsWith(" (edited since check)")) _summary.Text += " (edited since check)";
        }
    }

    public sealed class MainDecksDialog : Form
    {
        readonly TextBox _decks = new TextBox();
        readonly string _folder;

        public MainDecksDialog(string folder, IList<string> current)
        {
            _folder = folder;
            Text = "deckcheck: main decks";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 260);
            Label help = new Label();
            help.SetBounds(10, 10, 500, 50);
            help.Text = "Main decks for " + folder + "\r\nOne per line, relative to this folder. Leave empty to let "
                        + "deckcheck find them: the decks in this folder that include the file.";
            _decks.SetBounds(10, 64, 500, 150);
            _decks.Multiline = true;
            _decks.ScrollBars = ScrollBars.Vertical;
            _decks.Text = string.Join("\r\n", current.ToArray());
            Button browse = new Button(), ok = new Button(), cancel = new Button();
            browse.SetBounds(10, 224, 90, 26); browse.Text = "Browse...";
            ok.SetBounds(330, 224, 85, 26); ok.Text = "OK"; ok.DialogResult = DialogResult.OK;
            cancel.SetBounds(425, 224, 85, 26); cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel;
            browse.Click += delegate { Browse(); };
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.AddRange(new Control[] { help, _decks, browse, ok, cancel });
        }

        void Browse()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.InitialDirectory = _folder;
                d.Multiselect = true;
                d.Filter = "Keyword decks (*.k;*.key;*.dyn)|*.k;*.key;*.dyn|All files (*.*)|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                foreach (string f in d.FileNames)
                {
                    string rel = f.StartsWith(_folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                                 ? f.Substring(_folder.TrimEnd('\\').Length + 1) : f;
                    _decks.AppendText((_decks.Text.Length > 0 && !_decks.Text.EndsWith("\n") ? "\r\n" : "") + rel + "\r\n");
                }
            }
        }

        public IList<string> Decks
        {
            get { return _decks.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList(); }
        }
    }
}
