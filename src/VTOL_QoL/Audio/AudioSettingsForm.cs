using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;


// =========================================================================
// === НАСТРОЙКИ АУДИО ДЛЯ VR (ВЫБОР УСТРОЙСТВ) ============================
// =========================================================================
public class AudioSettingsForm : Form
{
    private ComboBox cmbPlayback;
    private ComboBox cmbCapture;
    private CheckBox chkRestoreAfterExit;
    private List<AudioDeviceInfo> playbackDevices;
    private List<AudioDeviceInfo> captureDevices;

    public AudioSettingsForm()
    {
        this.Text = "VTOL QoL — Аудио-устройства для VR";
        DarkWindowHelper.Apply(this);
        this.Size = new Size(520, 300);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(18, 22, 20);
        this.ForeColor = Color.FromArgb(0, 255, 102);
        this.Font = new Font("Consolas", 9.5f, FontStyle.Regular);

        Label l1 = new Label() { Text = "Устройство воспроизведения (наушники VR):", Left = 20, Top = 20, AutoSize = true, ForeColor = Color.LightGray };
        this.Controls.Add(l1);
        cmbPlayback = new ComboBox() { Left = 20, Top = 42, Width = 460, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 36, 33), ForeColor = Color.White };
        this.Controls.Add(cmbPlayback);

        Label l2 = new Label() { Text = "Устройство записи (микрофон VR):", Left = 20, Top = 85, AutoSize = true, ForeColor = Color.LightGray };
        this.Controls.Add(l2);
        cmbCapture = new ComboBox() { Left = 20, Top = 107, Width = 460, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 36, 33), ForeColor = Color.White };
        this.Controls.Add(cmbCapture);

        chkRestoreAfterExit = new CheckBox() { Text = "Восстанавливать прежние устройства после выхода из игры", Left = 20, Top = 150, Width = 460, Checked = SettingsManager.GetBool("RestoreAudioAfterExit", true), ForeColor = Color.FromArgb(210, 240, 220) };
        this.Controls.Add(chkRestoreAfterExit);

        Button btnAuto = new Button() { Text = "Автоопределение VR-устройств", Left = 20, Top = 190, Width = 230, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 110, 140), ForeColor = Color.White };
        btnAuto.Click += (s, e) => AutoDetect();
        this.Controls.Add(btnAuto);

        Button btnSave = new Button() { Text = "Сохранить", Left = 260, Top = 190, Width = 110, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 140, 60), ForeColor = Color.White, DialogResult = DialogResult.OK };
        btnSave.Click += (s, e) => SaveAndClose();
        this.Controls.Add(btnSave);

        Button btnCancel = new Button() { Text = "Отмена", Left = 380, Top = 190, Width = 100, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 50, 45), ForeColor = Color.White, DialogResult = DialogResult.Cancel };
        this.Controls.Add(btnCancel);

        LoadDevices();
    }

    private void LoadDevices()
    {
        playbackDevices = AudioInterop.GetDevices(EDataFlow.eRender);
        captureDevices = AudioInterop.GetDevices(EDataFlow.eCapture);
        cmbPlayback.Items.Clear();
        cmbCapture.Items.Clear();
        cmbPlayback.Items.Add("(не переключать)");
        cmbCapture.Items.Add("(не переключать)");
        foreach (AudioDeviceInfo d in playbackDevices) cmbPlayback.Items.Add(d.Name);
        foreach (AudioDeviceInfo d in captureDevices) cmbCapture.Items.Add(d.Name);

        string savedPlayback = SettingsManager.Get("VrPlaybackId", "");
        string savedCapture = SettingsManager.Get("VrCaptureId", "");
        cmbPlayback.SelectedIndex = 0;
        cmbCapture.SelectedIndex = 0;
        for (int i = 0; i < playbackDevices.Count; i++) if (playbackDevices[i].Id == savedPlayback) cmbPlayback.SelectedIndex = i + 1;
        for (int i = 0; i < captureDevices.Count; i++) if (captureDevices[i].Id == savedCapture) cmbCapture.SelectedIndex = i + 1;
    }

    private void AutoDetect()
    {
        AudioDeviceInfo? pb = AudioInterop.AutoDetectVr(playbackDevices);
        AudioDeviceInfo? cp = AudioInterop.AutoDetectVr(captureDevices);
        if (pb.HasValue)
        {
            for (int i = 0; i < playbackDevices.Count; i++) if (playbackDevices[i].Id == pb.Value.Id) cmbPlayback.SelectedIndex = i + 1;
        }
        if (cp.HasValue)
        {
            for (int i = 0; i < captureDevices.Count; i++) if (captureDevices[i].Id == cp.Value.Id) cmbCapture.SelectedIndex = i + 1;
        }
        if (!pb.HasValue && !cp.HasValue)
            MessageBox.Show("VR-устройства не найдены автоматически. Выберите вручную из списка.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveAndClose()
    {
        string pbId = cmbPlayback.SelectedIndex > 0 ? playbackDevices[cmbPlayback.SelectedIndex - 1].Id : "";
        string cpId = cmbCapture.SelectedIndex > 0 ? captureDevices[cmbCapture.SelectedIndex - 1].Id : "";
        SettingsManager.Set("VrPlaybackId", pbId);
        SettingsManager.Set("VrCaptureId", cpId);
        SettingsManager.SetBool("RestoreAudioAfterExit", chkRestoreAfterExit.Checked);
        SettingsManager.Save();
        this.DialogResult = DialogResult.OK;
        this.Close();
    }
}

