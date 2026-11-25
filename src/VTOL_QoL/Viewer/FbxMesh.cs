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
// === ЗАГРУЗЧИК FBX (РАЗДЕЛЕНИЕ КОРПУСА/ДЕТАЛЕЙ + АНТИ-Z-FIGHTING) =========
// =========================================================================
public class FbxSubMesh
{
    public List<System.Windows.Media.Media3D.Point3D> Vertices = new List<System.Windows.Media.Media3D.Point3D>();
    public List<int> Indices = new List<int>();
    public List<System.Windows.Point> UVs = new List<System.Windows.Point>();
}


public class FbxLightMarker
{
    public string Kind; // "NavRed", "NavGreen", "NavWhite", "Strobe", "Landing"
    public System.Windows.Media.Media3D.Point3D Position;
}


public class FbxMeshData
{
    public FbxSubMesh Body = new FbxSubMesh();
    public FbxSubMesh Details = new FbxSubMesh();
    public FbxSubMesh Glass = new FbxSubMesh();
    public List<FbxLightMarker> Lights = new List<FbxLightMarker>();

    public int TotalVertices
    {
        get { return Body.Vertices.Count + Details.Vertices.Count + Glass.Vertices.Count; }
    }
}

