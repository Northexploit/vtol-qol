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


public static class FbxLoader
{
    private class FbxNode
    {
        public string Name;
        public List<object> Properties = new List<object>();
        public List<FbxNode> Children = new List<FbxNode>();
    }

    private class FbxModelTransform
    {
        public long Id;
        public string Name;
        public double Tx, Ty, Tz, Rx, Ry, Rz;
        public double Sx = 1, Sy = 1, Sz = 1;
        public long ParentId = 0;
    }

    public static bool UseSecondUV = true;
    public static bool FlipUVY = true;

    private class FbxGeomPart
    {
        public long Id;
        public List<System.Windows.Media.Media3D.Point3D> RawVerts = new List<System.Windows.Media.Media3D.Point3D>();
        public List<int> RawPolyIndices = new List<int>();
        public List<System.Windows.Point> RawUVs = new List<System.Windows.Point>();
        public List<int> UVIndices = new List<int>();
        public string UVMapping = "ByPolygonVertex";
        public string UVRef = "Direct";
    }

    private static object GetProp(FbxNode n, int index)
    {
        if (n == null || n.Properties == null || index < 0 || index >= n.Properties.Count) return null;
        int cur = 0;
        foreach (object obj in n.Properties)
        {
            if (cur == index) return obj;
            cur++;
        }
        return null;
    }

    public static FbxMeshData Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length > 23 && Encoding.ASCII.GetString(data, 0, 18) == "Kaydara FBX Binary")
        {
            try { return LoadBinary(data); }
            catch (Exception ex) { AppLogger.Error("FBX", Path.GetFileName(path), ex); return null; }
        }
        return null;
    }

    private static FbxMeshData LoadBinary(byte[] data)
    {
        int pos = 27;
        uint version = BitConverter.ToUInt32(data, 23);
        bool is64 = version >= 7500;

        List<FbxNode> topNodes = new List<FbxNode>();
        while (pos < data.Length - 4)
        {
            FbxNode n = ReadNode(data, ref pos, is64);
            if (n == null) break;
            topNodes.Add(n);
        }

        FbxNode objects = FindChild(topNodes, "Objects");
        if (objects == null) return null;

        Dictionary<long, FbxGeomPart> geoms = new Dictionary<long, FbxGeomPart>();
        Dictionary<long, FbxModelTransform> models = new Dictionary<long, FbxModelTransform>();
        Dictionary<long, string> materials = new Dictionary<long, string>();
        Dictionary<long, long> geomToModel = new Dictionary<long, long>();
        Dictionary<long, List<string>> modelToMaterials = new Dictionary<long, List<string>>();

        foreach (FbxNode child in objects.Children)
        {
            if (child.Name == "Geometry" && child.Properties.Count > 0)
            {
                long id = Convert.ToInt64(GetProp(child, 0));
                FbxGeomPart part = ParseGeometryNode(child, id);
                if (part != null && part.RawVerts.Count > 0) geoms[id] = part;
            }
            else if (child.Name == "Model" && child.Properties.Count > 0)
            {
                long id = Convert.ToInt64(GetProp(child, 0));
                models[id] = ParseModelNode(child, id);
            }
            else if (child.Name == "Material" && child.Properties.Count > 1)
            {
                long id = Convert.ToInt64(GetProp(child, 0));
                materials[id] = GetProp(child, 1) as string ?? "";
            }
        }

        FbxNode conns = FindChild(topNodes, "Connections");
        if (conns != null)
        {
            foreach (FbxNode c in conns.Children)
            {
                if (c.Name == "C" && c.Properties.Count >= 3 && (GetProp(c, 0) as string) == "OO")
                {
                    long childId = Convert.ToInt64(GetProp(c, 1));
                    long parentId = Convert.ToInt64(GetProp(c, 2));
                    if (geoms.ContainsKey(childId) && models.ContainsKey(parentId))
                        geomToModel[childId] = parentId;
                    else if (models.ContainsKey(childId) && models.ContainsKey(parentId))
                        models[childId].ParentId = parentId;
                    else if (materials.ContainsKey(childId) && models.ContainsKey(parentId))
                    {
                        List<string> list;
                        if (!modelToMaterials.TryGetValue(parentId, out list))
                        {
                            list = new List<string>();
                            modelToMaterials[parentId] = list;
                        }
                        list.Add(materials[childId]);
                    }
                }
            }
        }

        return AssembleFullMesh(geoms, models, geomToModel, modelToMaterials);
    }

    private static string GetFullHierarchyName(long modelId, Dictionary<long, FbxModelTransform> models, Dictionary<long, List<string>> modelToMaterials)
    {
        StringBuilder sb = new StringBuilder();
        HashSet<long> visited = new HashSet<long>();
        long cur = modelId;
        while (cur != 0 && models.ContainsKey(cur) && !visited.Contains(cur))
        {
            visited.Add(cur);
            FbxModelTransform m = models[cur];
            if (!string.IsNullOrEmpty(m.Name)) sb.Append(" ").Append(m.Name);
            List<string> mats;
            if (modelToMaterials.TryGetValue(cur, out mats))
                foreach (string mat in mats) sb.Append(" ").Append(mat);
            cur = m.ParentId;
        }
        return sb.ToString();
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (string kw in keywords)
            if (text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static FbxMeshData AssembleFullMesh(
        Dictionary<long, FbxGeomPart> geoms,
        Dictionary<long, FbxModelTransform> models,
        Dictionary<long, long> geomToModel,
        Dictionary<long, List<string>> modelToMaterials)
    {
        bool hasDetailedParts = false;
        foreach (var kv in geoms) if (kv.Value.RawVerts.Count > 30) { hasDetailedParts = true; break; }

        FbxMeshData result = new FbxMeshData();
        HashSet<string> seenGeometrySignatures = new HashSet<string>();

        foreach (var kv in geoms)
        {
            FbxGeomPart part = kv.Value;
            if (hasDetailedParts && !(part.RawVerts.Count > 24))
            {
                string quickName = "";
                long tmpId;
                if (geomToModel.TryGetValue(part.Id, out tmpId))
                    quickName = GetFullHierarchyName(tmpId, models, modelToMaterials);
                if (!ContainsAny(quickName, "Rotor", "Blade", "Prop"))
                    continue;
            }

            System.Windows.Media.Media3D.Matrix3D worldMat = System.Windows.Media.Media3D.Matrix3D.Identity;
            string fullTag = "";
            long modelId;
            string selfName = "";
            if (geomToModel.TryGetValue(part.Id, out modelId))
            {
                if (models.ContainsKey(modelId) && !string.IsNullOrEmpty(models[modelId].Name))
                    selfName = models[modelId].Name;

                fullTag = GetFullHierarchyName(modelId, models, modelToMaterials);

                // 1. Отсекаем все повреждённые дубликаты (_damaged, _dmg, _destroyed, _broken, dmg1..4),
                // негнущиеся дубли (_unbent), дальние LOD (кроме двигателей vgEngineLowPoly!),
                // осколки стекла, царапины, аватарки, камеры и шлем/руки пилота
                bool isEngineNozzle = ContainsAny(fullTag, "vgEngine", "mat_vgLowpoly", "leftEngine", "rightEngine", "t55-engine");

                if (!isEngineNozzle)
                {
                    if (ContainsAny(selfName,
                        "_damaged", "_dmg", "_destroyed", ".destroyed", "_broken", "tailBroken", "brokenWing",
                        "LOD 1", "LOD 2", "LOD 3", "LOD 4", "LOD1", "LOD2", "LOD3", "LOD4",
                        "LowPoly", "_lod", "GlassBreak", ".scratch", "_glare", "hudGlare", "hudScratch",
                        "steamImg", "TailArt", "Collider", "Hitbox", "Trigger", "Shadow",
                        "Flame", "Afterburner", "ExhaustCone", "JetFlame", "NavLight", "Strobe", "LandingLight", "TaxiLight"))
                        continue;

                    if (ContainsAny(fullTag,
                        "_damaged", "_dmg", "_destroyed", ".destroyed", "_broken", "tailBroken", "brokenWing",
                        "dmg1", "dmg2", "dmg3", "dmg4", "Blurred",
                        "sevtfLowPoly", "body_LOD2", "LOD 1", "LOD1", "LOD 2", "LOD2", "LOD3", "LowPoly",
                        "steamUserImage", "FlybyCam", "SpectatorCam", "Camcorder", "Remote", "BobblePilot",
                        "[CameraRig]", "Camera (eye)", "Helmet", "hqh", "flesh", "headSphere", "hmdVisor", "tintVisor",
                        "DashCanvasFront", "DashCanvasRear", "PassengerOnlyObjs", "LowerCenterDash"))
                        continue;
                }


                worldMat = BuildWorldMatrix(modelId, models);
            }

            List<System.Windows.Media.Media3D.Point3D> worldVerts = new List<System.Windows.Media.Media3D.Point3D>(part.RawVerts.Count);
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            for (int i = 0; i < part.RawVerts.Count; i++)
            {
                System.Windows.Media.Media3D.Point3D wp = worldMat.Transform(part.RawVerts[i]);
                worldVerts.Add(wp);
                if (wp.X < minX) minX = wp.X; if (wp.X > maxX) maxX = wp.X;
                if (wp.Y < minY) minY = wp.Y; if (wp.Y > maxY) maxY = wp.Y;
                if (wp.Z < minZ) minZ = wp.Z; if (wp.Z > maxZ) maxZ = wp.Z;
            }

            if (part.RawVerts.Count <= 6 && part.RawPolyIndices.Count <= 6)
                continue;

            string sig = string.Format("{0}_{1}_{2:F3}_{3:F3}_{4:F3}_{5:F3}_{6:F3}_{7:F3}",
                part.RawVerts.Count, part.RawPolyIndices.Count,
                minX, minY, minZ, maxX, maxY, maxZ);
            if (seenGeometrySignatures.Contains(sig))
            {
                if (ContainsAny(fullTag, "T-55") && ContainsAny(fullTag, "flap", "wing", "body", "airbrake"))
                    AppLogger.Log("T55_DUP", string.Format("Self='{0}', Verts={1}, Tag='{2}'", selfName.Trim(), part.RawVerts.Count, fullTag.Trim()));
                continue;
            }
            seenGeometrySignatures.Add(sig);

            if (ContainsAny(fullTag, "EF-24", "ef24", "ew"))
                AppLogger.Log("EF24_PART", string.Format("Self='{0}' | Verts={1} | BBox=({2:F1},{3:F1},{4:F1})..({5:F1},{6:F1},{7:F1}) | Tag='{8}'",
                    selfName.Trim(), part.RawVerts.Count, minX, minY, minZ, maxX, maxY, maxZ, fullTag.Trim()));



            FbxSubMesh targetSub = result.Body;

            // 2. Стекло фонаря кабины (полупрозрачный плексиглас)
            bool isGlassMesh = ContainsAny(selfName, "Canopy", "Glass", "Windshield", "Transparent") &&
                               !ContainsAny(selfName, "Frame", "Cylinder", "Piston", "Switch", "Button", "Sensor", "Tads", "Optic", "Camera", "Turret");
            bool isGlassMat = ContainsAny(fullTag, "mat_f45CanopyExt", "mat_ewCanopy", "CanopyGlass", "CockpitGlass", "HudGlass");

            if ((isGlassMesh || isGlassMat) && !ContainsAny(selfName, "Frame", "detCord", "Cylinder", "Piston"))
            {
                targetSub = result.Glass;
            }
            // 3. Сопла двигателей (vgEngineLowPoly, _engine), стойки/колёса шасси (_ext2, landingGear), интерьер кабины (_int, cockpitProps, acesSeat)
            else if (part.RawUVs.Count == 0 || (!ContainsAny(selfName, "gearDoor", "GearDoor", "centerRails", "sidePylons", "sideRails", "UnderRailNoseCone") && ContainsAny(fullTag,
    "vgEngine", "mat_vgLowpoly", "_ext2", "_engine", "_int", "Interior", "cockpitProps", "NonUIDash", "DashCanvas", "Instruments",
                "acesSeat", "EjectorSeat", "Seat", "Stick", "Throttle", "Pedal", "MFD", "Switch", "Button", "Knob", "Gauge",
                "LandingGear", "landingGear", "FrontGear", "LeftGear", "RightGear", "Wheel", "Tire", "Tyre", "Strut", "Piston", "catHook",
                "Nozzle", "gimbalFin", "Turbine", "Compressor", "liftFanCylinder", "liftFanPiston", "IntakeGuides",
                "mat_tgp", "eyeHolder", "Pylon", "Rail", "Launcher", "Rack", "Hardpoint", "Missile", "Bomb", "tempMirror")))
            {
                targetSub = result.Details;
            }

            List<int> polyVerts = new List<int>();
            List<int> polyCorners = new List<int>();
            int cornerIdx = 0;

            foreach (int raw in part.RawPolyIndices)
            {
                bool last = raw < 0;
                int vIdx = last ? (-raw - 1) : raw;
                polyVerts.Add(vIdx);
                polyCorners.Add(cornerIdx);
                cornerIdx++;

                if (last)
                {
                    for (int k = 1; k + 1 < polyVerts.Count; k++)
                    {
                        int v0 = 0; foreach (int x in polyVerts) { v0 = x; break; }
                        int c0 = 0; foreach (int x in polyCorners) { c0 = x; break; }
                        int[] triV = new int[] { v0, polyVerts[k], polyVerts[k + 1] };
                        int[] triC = new int[] { c0, polyCorners[k], polyCorners[k + 1] };

                        for (int t = 0; t < 3; t++)
                        {
                            int vi = triV[t];
                            if (vi < 0 || vi >= worldVerts.Count) continue;

                            targetSub.Indices.Add(targetSub.Vertices.Count);
                            targetSub.Vertices.Add(worldVerts[vi]);

                            System.Windows.Point uv = ResolveUV(part, vi, triC[t]);
                            targetSub.UVs.Add(new System.Windows.Point(uv.X, FlipUVY ? (1.0 - uv.Y) : uv.Y));
                        }
                    }
                    polyVerts.Clear();
                    polyCorners.Clear();
                }
            }
        }

        foreach (var mkv in models)
        {
            FbxModelTransform m = mkv.Value;
            string mName = m.Name ?? "";
            string hName = GetFullHierarchyName(m.Id, models, modelToMaterials);

            // Исключаем повреждённые дубли, тумблеры в кабине (Switch, Interactable, DashCanvas)
            // и пустые корневые контейнеры-контроллеры (Controller, ExtLights, LightSwitches)
            if (ContainsAny(hName,
    "_damaged", "_dmg", "_destroyed", ".destroyed", "_broken",
    "LOD 2", "LOD2", "Folded", "BrokenWing", "Camcorder", "Spectator",
    "Switch", "Interactable", "Controller", "DashCanvas", "Overhead",
    "LightSwitches"))
                continue;

            // Пропускаем только пустые родительские группы, но не их дочерние лампы!
            if (ContainsAny(mName, "NavLights", "StrobeLights", "ExtLights", "LightObjs"))
                continue;

            string kind = null;
            if (ContainsAny(mName, "RedStrobe", "redStrobe", "Beacon", "AntiCol"))
            {
                if (!ContainsAny(hName, "ah94", "AH-94")) continue;
                kind = "RedStrobe";
            }

            else if (ContainsAny(mName, "Strobe"))
                kind = "Strobe";
            else if (ContainsAny(mName, "LandingLight", "TaxiLight", "NoseLight", "Spotlight"))
                kind = "Landing";
            else if (ContainsAny(mName, "NavLight", "Nav_L", "Nav_R", "WingLight", "TailLight"))
            {
                if (ContainsAny(mName, "Tail", "White", "Rear")) kind = "NavWhite";
                else kind = "NavAuto";
            }


            if (kind != null)
            {
                System.Windows.Media.Media3D.Matrix3D wm = BuildWorldMatrix(m.Id, models);
                System.Windows.Media.Media3D.Point3D pos = wm.Transform(new System.Windows.Media.Media3D.Point3D(0, 0, 0));
                if (kind == "RedStrobe" && Math.Abs(pos.X) > 450.0) continue;
                if (pos.X * pos.X + pos.Y * pos.Y + pos.Z * pos.Z < 1.0) continue;

                // Проверяем дубли только среди огней ТОГО ЖЕ типа (чтобы строб не склеивался с БАНО!)
                bool dup = false;
                for (int i = 0; i < result.Lights.Count; i++)
                {
                    if (result.Lights[i].Kind == kind && (result.Lights[i].Position - pos).LengthSquared < 16000.0)
                    {
                        dup = true;
                        break;
                    }
                }
                if (!dup)
                    result.Lights.Add(new FbxLightMarker { Kind = kind, Position = pos });
            }
        }


        if (result.Body.Vertices.Count == 0 && result.Details.Vertices.Count > 0)
        {
            result.Body = result.Details;
            result.Details = new FbxSubMesh();
        }

        return result.TotalVertices > 0 ? result : null;
    }

    private static System.Windows.Point ResolveUV(FbxGeomPart part, int vertIdx, int cornerIdx)
    {
        if (part.RawUVs.Count == 0) return new System.Windows.Point(0, 0);

        int lookup = (part.UVMapping == "ByVertice") ? vertIdx : cornerIdx;
        if (part.UVIndices.Count == 0 && part.RawUVs.Count == part.RawVerts.Count)
            lookup = vertIdx;

        int uvIdx = lookup;
        if (part.UVIndices.Count > 0 && lookup >= 0 && lookup < part.UVIndices.Count)
            uvIdx = part.UVIndices[lookup];

        if (uvIdx >= 0 && uvIdx < part.RawUVs.Count) return part.RawUVs[uvIdx];
        if (vertIdx >= 0 && vertIdx < part.RawUVs.Count) return part.RawUVs[vertIdx];
        return new System.Windows.Point(0, 0);
    }

    private static System.Windows.Media.Media3D.Matrix3D BuildWorldMatrix(long modelId, Dictionary<long, FbxModelTransform> models)
    {
        System.Windows.Media.Media3D.Matrix3D mat = System.Windows.Media.Media3D.Matrix3D.Identity;
        HashSet<long> visited = new HashSet<long>();
        long cur = modelId;
        while (cur != 0 && models.ContainsKey(cur) && !visited.Contains(cur))
        {
            visited.Add(cur);
            FbxModelTransform m = models[cur];
            System.Windows.Media.Media3D.Transform3DGroup tg = new System.Windows.Media.Media3D.Transform3DGroup();
            tg.Children.Add(new System.Windows.Media.Media3D.ScaleTransform3D(m.Sx, m.Sy, m.Sz));
            tg.Children.Add(new System.Windows.Media.Media3D.RotateTransform3D(new System.Windows.Media.Media3D.AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(1, 0, 0), m.Rx)));
            tg.Children.Add(new System.Windows.Media.Media3D.RotateTransform3D(new System.Windows.Media.Media3D.AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(0, 1, 0), m.Ry)));
            tg.Children.Add(new System.Windows.Media.Media3D.RotateTransform3D(new System.Windows.Media.Media3D.AxisAngleRotation3D(new System.Windows.Media.Media3D.Vector3D(0, 0, 1), m.Rz)));
            tg.Children.Add(new System.Windows.Media.Media3D.TranslateTransform3D(m.Tx, m.Ty, m.Tz));
            mat = System.Windows.Media.Media3D.Matrix3D.Multiply(mat, tg.Value);
            cur = m.ParentId;
        }
        return mat;
    }

    private static FbxModelTransform ParseModelNode(FbxNode node, long id)
    {
        FbxModelTransform tr = new FbxModelTransform { Id = id };

        if (node.Properties.Count > 1) tr.Name = node.Properties[1] as string;
    FbxNode p70 = FindChild(node.Children, "Properties70");
    if (p70 != null)
    {
        foreach (FbxNode p in p70.Children)
        {
            if (p.Name == "P" && p.Properties.Count >= 7)
            {
                string propName = p.Properties[0] as string;
                double x = Convert.ToDouble(p.Properties[4]);
                double y = Convert.ToDouble(p.Properties[5]);
                double z = Convert.ToDouble(p.Properties[6]);
                if (propName == "Lcl Translation") { tr.Tx = x; tr.Ty = y; tr.Tz = z; }
                else if (propName == "Lcl Rotation") { tr.Rx = x; tr.Ry = y; tr.Rz = z; }
                else if (propName == "Lcl Scaling") { tr.Sx = x; tr.Sy = y; tr.Sz = z; }
            }
        }
    }
    return tr;
}
    private static bool HasValidUVData(FbxNode uvNode)
    {
        if (uvNode == null) return false;
        FbxNode uvData = FindChild(uvNode.Children, "UV");
        if (uvData == null || uvData.Properties.Count == 0) return false;
        double[] arr = GetProp(uvData, 0) as double[];
        if (arr == null || arr.Length < 4) return false;
        for (int i = 0; i < arr.Length; i++)
            if (Math.Abs(arr[i]) > 0.0001) return true;
        return false;
    }

    private static FbxGeomPart ParseGeometryNode(FbxNode geometry, long id)
{
        FbxGeomPart part = new FbxGeomPart { Id = id };
        FbxNode verticesNode = FindChild(geometry.Children, "Vertices");
        FbxNode indicesNode = FindChild(geometry.Children, "PolygonVertexIndex");
        List<FbxNode> uvLayers = new List<FbxNode>();
        foreach (FbxNode ch in geometry.Children)
            if (ch.Name == "LayerElementUV") uvLayers.Add(ch);

        FbxNode uvNode = null;
        if (uvLayers.Count > 0)
        {
            int idx = (UseSecondUV && uvLayers.Count > 1) ? 1 : 0;
            uvNode = uvLayers[idx];
            // Если выбранный UV-слой пустой или все координаты в (0,0) — переключаемся на другой слой
            if (uvLayers.Count > 1 && !HasValidUVData(uvNode))
                uvNode = uvLayers[1 - idx];
        }



        if (verticesNode != null && verticesNode.Properties.Count > 0)
    {
        double[] arr = verticesNode.Properties[0] as double[];
        if (arr != null)
            for (int i = 0; i + 2 < arr.Length; i += 3)
                part.RawVerts.Add(new System.Windows.Media.Media3D.Point3D(arr[i], arr[i + 1], arr[i + 2]));
    }

    if (indicesNode != null && indicesNode.Properties.Count > 0)
    {
        int[] arr = indicesNode.Properties[0] as int[];
        if (arr != null) part.RawPolyIndices.AddRange(arr);
    }

    if (uvNode != null)
    {
        FbxNode mapNode = FindChild(uvNode.Children, "MappingInformationType");
        if (mapNode != null && mapNode.Properties.Count > 0) part.UVMapping = mapNode.Properties[0] as string ?? "ByPolygonVertex";

        FbxNode refNode = FindChild(uvNode.Children, "ReferenceInformationType");
        if (refNode != null && refNode.Properties.Count > 0) part.UVRef = refNode.Properties[0] as string ?? "Direct";

        FbxNode uvData = FindChild(uvNode.Children, "UV");
        if (uvData != null && uvData.Properties.Count > 0)
        {
            double[] arr = uvData.Properties[0] as double[];
            if (arr != null)
                for (int i = 0; i + 1 < arr.Length; i += 2)
                    part.RawUVs.Add(new System.Windows.Point(arr[i], arr[i + 1]));
        }

        FbxNode uvIdxData = FindChild(uvNode.Children, "UVIndex");
        if (uvIdxData != null && uvIdxData.Properties.Count > 0)
        {
            int[] arr = uvIdxData.Properties[0] as int[];
            if (arr != null) part.UVIndices.AddRange(arr);
        }
    }
    return part;
}

private static FbxNode FindChild(List<FbxNode> nodes, string name)
{
    foreach (FbxNode n in nodes) if (n.Name == name) return n;
    return null;
}

private static FbxNode ReadNode(byte[] data, ref int pos, bool is64)
{
    long endOffset, numProps, propListLen;
    if (is64)
    {
        endOffset = BitConverter.ToInt64(data, pos); pos += 8;
        numProps = BitConverter.ToInt64(data, pos); pos += 8;
        propListLen = BitConverter.ToInt64(data, pos); pos += 8;
    }
    else
    {
        endOffset = BitConverter.ToUInt32(data, pos); pos += 4;
        numProps = BitConverter.ToUInt32(data, pos); pos += 4;
        propListLen = BitConverter.ToUInt32(data, pos); pos += 4;
    }
    byte nameLen = data[pos]; pos += 1;
    if (endOffset == 0 && numProps == 0 && propListLen == 0 && nameLen == 0) return null;
    string name = Encoding.ASCII.GetString(data, pos, nameLen); pos += nameLen;

    FbxNode node = new FbxNode { Name = name };
    for (int i = 0; i < numProps; i++) node.Properties.Add(ReadProperty(data, ref pos));
    while (pos < endOffset)
    {
        FbxNode child = ReadNode(data, ref pos, is64);
        if (child == null) break;
        node.Children.Add(child);
    }
    pos = (int)endOffset;
    return node;
}

private static object ReadProperty(byte[] data, ref int pos)
{
    char type = (char)data[pos]; pos += 1;
    switch (type)
    {
        case 'Y': { short v = BitConverter.ToInt16(data, pos); pos += 2; return (int)v; }
        case 'C': { bool v = data[pos] != 0; pos += 1; return v; }
        case 'I': { int v = BitConverter.ToInt32(data, pos); pos += 4; return v; }
        case 'F': { float v = BitConverter.ToSingle(data, pos); pos += 4; return v; }
        case 'D': { double v = BitConverter.ToDouble(data, pos); pos += 8; return v; }
        case 'L': { long v = BitConverter.ToInt64(data, pos); pos += 8; return v; }
        case 'f': return ReadArray(data, ref pos, 4, true);
        case 'd': return ReadArray(data, ref pos, 8, true);
        case 'l': return ReadArray(data, ref pos, 8, false);
        case 'i': return ReadArray(data, ref pos, 4, false);
        case 'b': return ReadArray(data, ref pos, 1, false);
        case 'S': { int len = BitConverter.ToInt32(data, pos); pos += 4; string s = Encoding.UTF8.GetString(data, pos, len); pos += len; return s; }
        case 'R': { int len = BitConverter.ToInt32(data, pos); pos += 4; pos += len; return null; }
        default: throw new Exception("Неизвестный тип свойства FBX: " + type);
    }
}

private static object ReadArray(byte[] data, ref int pos, int elemSize, bool isFloat)
{
    int arrayLen = BitConverter.ToInt32(data, pos); pos += 4;
    int encoding = BitConverter.ToInt32(data, pos); pos += 4;
    int compressedLen = BitConverter.ToInt32(data, pos); pos += 4;

    byte[] raw;
    if (encoding == 1)
    {
        byte[] compressed = new byte[compressedLen];
        Array.Copy(data, pos, compressed, 0, compressedLen);
        pos += compressedLen;
        raw = ZlibDecompress(compressed);
    }
    else
    {
        raw = new byte[arrayLen * elemSize];
        Array.Copy(data, pos, raw, 0, raw.Length);
        pos += raw.Length;
    }

    if (elemSize == 8 && isFloat) { double[] r = new double[arrayLen]; for (int i = 0; i < arrayLen; i++) r[i] = BitConverter.ToDouble(raw, i * 8); return r; }
    if (elemSize == 4 && isFloat) { float[] r = new float[arrayLen]; for (int i = 0; i < arrayLen; i++) r[i] = BitConverter.ToSingle(raw, i * 4); return r; }
    if (elemSize == 4 && !isFloat) { int[] r = new int[arrayLen]; for (int i = 0; i < arrayLen; i++) r[i] = BitConverter.ToInt32(raw, i * 4); return r; }
    if (elemSize == 8 && !isFloat) { long[] r = new long[arrayLen]; for (int i = 0; i < arrayLen; i++) r[i] = BitConverter.ToInt64(raw, i * 8); return r; }
    return null;
}
private static byte[] ZlibDecompress(byte[] compressed)
{
    using (MemoryStream ms = new MemoryStream(compressed, 2, compressed.Length - 2))
    using (System.IO.Compression.DeflateStream ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
    using (MemoryStream outMs = new MemoryStream())
    {
        byte[] buf = new byte[8192];
        int read;
        while ((read = ds.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, read);
        return outMs.ToArray();
    }
}
}

