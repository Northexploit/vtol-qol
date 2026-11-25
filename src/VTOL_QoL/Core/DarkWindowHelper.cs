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
// === ТЁМНАЯ РАМКА WINDOWS ДЛЯ ВСЕХ ДИАЛОГОВЫХ ОКОН (DWM DARK TITLE BAR) ==
// =========================================================================
public static class DarkWindowHelper
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void Apply(Form form)
    {
        if (form == null) return;
        Action applyAttr = () => {
            try
            {
                int useDark = 1;
                if (DwmSetWindowAttribute(form.Handle, 20, ref useDark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref useDark, sizeof(int));
            }
            catch { }
        };
        if (form.IsHandleCreated) applyAttr();
        form.HandleCreated += (s, e) => applyAttr();
    }
}

