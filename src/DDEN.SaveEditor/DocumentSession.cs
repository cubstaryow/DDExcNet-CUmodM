using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace DDEN.SaveEditor
{
    internal sealed partial class SaveDocument
    {
        private JObject _baseline;
        private string _sourceHash;
        internal IEnumerable<FieldRow> EditableRows => Rows.Concat(Limbs.SelectMany(l => l.Rows))
            .Concat(Items.SelectMany(i => new[] { i.Condition, i.Favourited, i.Label }.Concat(i.PropertyRows))).Where(r => r != null);
        private static string FileHash(string path)
        { using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(stream)); }
        private void SessionLoaded(string hash = null)
        { _baseline = (JObject)Doc.DeepClone(); _sourceHash = hash ?? FileHash(Path); }
        internal List<SaveChange> Changes() => _baseline == null || Doc == null ? new List<SaveChange>() : SaveChanges.Compare(_baseline, Doc);
        internal bool ValidateForSave()
        {
            var invalid = EditableRows.FirstOrDefault(r => !string.IsNullOrEmpty(r.Error));
            if (invalid != null) { SetStatus("请先修正无效输入：" + EditorLabels.Field(invalid), true); return false; }
            string propertyError = ValidateItemProperties();
            if (propertyError != null) { SetStatus(propertyError, true); return false; }
            try
            {
                if (_sourceHash != null && _sourceHash != FileHash(Path))
                { SetStatus("存档文件已在外部更新。请先重新读取，避免覆盖新的游戏进度。", true); return false; }
            }
            catch (Exception ex) { SetStatus("无法核对存档文件：" + ex.Message, true); return false; }
            return true;
        }
        internal void DiscardChanges()
        {
            if (_baseline == null) return;
            Doc = (JObject)_baseline.DeepClone(); Dirty = false; Rebuild();
            SetStatus("已撤销本次尚未保存的修改。", false);
        }
    }
}
