using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// =========================================================================
// === ДИАЛОГ СОГЛАСИЯ НА АВТОПОЧИНКУ ЛИВРЕЙ ==============================
// Программа ничего не удаляет и не отписывает без явного согласия:
// пользователь видит список действий и снимает те, которые не нужны.
// =========================================================================
public class LiveryFixOption
{
    public string Key;      // внутренний код действия
    public string Title;    // текст для пользователя
    public int Count;       // сколько объектов затронуто
    public bool Checked;    // по умолчанию включено?
}

public class LiveryFixForm : Form
{
    public List<string> AcceptedKeys = new List<string>();
    private CheckedListBox list;

    public LiveryFixForm(List<LiveryFixOption> options)
    {
        this.Text = "VTOL QoL — автопочинка ливрей";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ClientSize = new Size(660, 430);
        this.BackColor = Color.FromArgb(22, 26, 24);

        Label lblHead = new Label()
        {
            Left = 14, Top = 12, Width = 632, Height = 52,
            ForeColor = Color.FromArgb(220, 240, 225),
            Text = "Найдены проблемы с ливреями. Отметьте, что исправить автоматически.\n"
                 + "Снятые галочки означают «не трогать». Только после нажатия «Исправить» что-то изменится."
        };
        this.Controls.Add(lblHead);

        list = new CheckedListBox();
        list.Left = 14; list.Top = 68; list.Width = 632; list.Height = 300;
        list.BackColor = Color.FromArgb(18, 22, 20);
        list.ForeColor = Color.White;
        list.BorderStyle = BorderStyle.FixedSingle;
        list.CheckOnClick = true;
        list.Font = new Font("Consolas", 9.5f);

        foreach (LiveryFixOption o in options)
        {
            int idx = list.Items.Add(o.Count > 0
                ? (o.Title + "  (" + o.Count + ")")
                : o.Title);
            list.SetItemChecked(idx, o.Checked);
        }
        this.Controls.Add(list);

        Button btnApply = new Button()
        {
            Text = "Исправить", Left = 400, Top = 382, Width = 120, Height = 32,
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 110, 90), ForeColor = Color.White,
            DialogResult = DialogResult.OK
        };
        this.Controls.Add(btnApply);

        Button btnCancel = new Button()
        {
            Text = "Отмена", Left = 526, Top = 382, Width = 120, Height = 32,
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 50, 46), ForeColor = Color.White,
            DialogResult = DialogResult.Cancel
        };
        this.Controls.Add(btnCancel);

        this.AcceptButton = btnApply;
        this.CancelButton = btnCancel;

        btnApply.Click += (s, e) =>
        {
            AcceptedKeys.Clear();
            for (int i = 0; i < list.Items.Count && i < options.Count; i++)
                if (list.GetItemChecked(i)) AcceptedKeys.Add(options[i].Key);
        };
    }
}