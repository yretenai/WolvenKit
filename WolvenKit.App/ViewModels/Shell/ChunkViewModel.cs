using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using DynamicData;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using WolvenKit.Common.Services;
using WolvenKit.RED4.Archive;
using WolvenKit.RED4.Types;
using WolvenKit.RED4.CR2W;
using WolvenKit.RED4.Archive.CR2W;
using WolvenKit.RED4.Archive.IO;
using System.Windows.Input;
using WolvenKit.Functionality.Commands;
using static WolvenKit.RED4.Types.RedReflection;
using WolvenKit.Common.Conversion;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using System.Windows.Forms;
using System.IO;
using System.Dynamic;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text;
using System.Reactive;
using WolvenKit.RED4.Archive.Buffer;
using WolvenKit.ViewModels.Documents;
using Splat;
using WolvenKit.ViewModels.Dialogs;

namespace WolvenKit.ViewModels.Shell
{
    public class ChunkViewModel : ReactiveObject, ISelectableTreeViewItemModel
    {
        [ObservableAsProperty] public string Value { get; }
        [ObservableAsProperty] public bool IsDefault { get; }
        [ObservableAsProperty] public ObservableCollection<ChunkViewModel> Properties { get; }
        [ObservableAsProperty] public IRedType PropertyGridData { get; }

        #region Constructors

        public ChunkViewModel(IRedType export, ChunkViewModel parent = null, string name = null)
        {
            Data = export;
            Parent = parent;
            propertyName = name;

            this.WhenAnyValue(x => x.Data)
                .Select(_ => CalculateValue())
                .ToPropertyEx(this, x => x.Value);
            this.WhenAnyValue(x => x.Data)
                .Select(_ => CalculateIsDefault())
                .ToPropertyEx(this, x => x.IsDefault);
            if (Parent != null)
            {
                this.WhenAnyValue(x => x.Data, x => x.Parent.IsExpanded)
                    .Where(_ => Parent.IsExpanded)
                    .Select(_ => CalculateProperties())
                    .ToPropertyEx(this, x => x.Properties);
            }
            else
            {
                this.WhenAnyValue(x => x.Data)
                    .Select(_ => CalculateProperties())
                    .ToPropertyEx(this, x => x.Properties);
            }

            //this.WhenAnyValue(x => x.Data)
            //    .Select(x => CalculatePropertyGridData())
            //    .ToPropertyEx(this, x => x.PropertyGridData);
            this.WhenAnyValue(x => x.Data)
                .Subscribe((_) =>
                {
                    if (Parent != null && propertyName != null && Data is not IRedBaseHandle)
                    {
                        Type parentType = Parent.PropertyType;
                        var parentData = Parent.Data;
                        if (Parent.Data is IRedBaseHandle handle && handle != null)
                        {
                            parentData = handle.GetValue();
                            parentType = handle.GetValue().GetType();
                        }
                        var epi = GetPropertyByRedName(parentType, propertyName);
                        if (epi != null)
                        {
                            epi.SetValue((IRedClass)parentData, Data);
                        }
                    }
                    //Parent.RaisePropertyChanged("Data");
                });

            OpenRefCommand = new DelegateCommand(p => ExecuteOpenRef(), (p) => CanOpenRef());
            ExportChunkCommand = new DelegateCommand((p) => ExecuteExportChunk(), (p) => CanExportChunk());
            AddItemToArrayCommand = new DelegateCommand((p) => ExecuteAddItemToArray(), (p) => CanAddItemToArray());
            AddItemToCompiledDataCommand = new DelegateCommand((p) => ExecuteAddItemToCompiledData(), (p) => CanAddItemToCompiledData());
            DeleteItemCommand = new DelegateCommand((p) => ExecuteDeleteItem(), (p) => CanDeleteItem());
            OpenChunkCommand = new DelegateCommand((p) => ExecuteOpenChunk(), (p) => CanOpenChunk());
        }

        public ChunkViewModel(IRedType export, RDTDataViewModel tab) : this(export)
        {
            _tab = tab;
            IsExpanded = true;
            Data = export;
            this.RaisePropertyChanged("Data");
            this.WhenAnyValue(x => x.Data).Skip(1).Subscribe((x) =>
            {
                Tab.File.SetIsDirty(true);
            });
        }

        #endregion Constructors

        #region Properties

        private RDTDataViewModel _tab;

        public RDTDataViewModel Tab
        {
            get
            {
                if (_tab != null)
                    return _tab;
                return Parent.Tab;
            }
        }

        [Reactive] public IRedType Data { get; set; }

        public ChunkViewModel Parent { get; set; }

        public object DisplayProperties
        {
            get
            {
                if (Properties == null || Properties.Count == 0)
                    return new ObservableCollection<object>(new[] {
                       this
                    });
                else
                    return Properties;
            }
        }

        public class RedArrayWrapper : IRedType
        {
            private IRedArray list;

            [TypeConverter(typeof(ExpandableObjectConverter))]
            public Dictionary<string, object> Properties { get; set; }

            public RedArrayWrapper(IRedArray ary)
            {
                list = ary;
                Properties = new Dictionary<string, object>();
                foreach (var item in ary)
                {
                    if (item is IRedBaseHandle hnd)
                    {
                        var star = hnd.GetValue();
                        Properties.Add(ary.IndexOf(item).ToString(), star);
                    }
                    else
                    {
                        Properties.Add(ary.IndexOf(item).ToString(), item);
                    }
                }
            }
        }

        public class RedArrayItem<T> : IRedType
        {
            private IRedArray list;
            private int index;
            public T Value
            {
                get => (T)list[index];
                set => list[index] = value;
            }

            public RedArrayItem(IRedArray ary, int i)
            {
                list = ary;
                index = i;
            }
        }

        public class RedClassProperty<T> : IRedType
        {
            private IRedClass obj;
            private string propertyName;
            public T Value
            {
                get
                {
                    var epi = GetPropertyByRedName(obj.GetType(), propertyName);
                    if (epi != null)
                    {
                        return (T)epi.GetValue(obj);
                    }
                    return default(T);
                }
                set {
                    var epi = GetPropertyByRedName(obj.GetType(), propertyName);
                    if (epi != null)
                    {
                        epi.SetValue(obj, value);
                    }
                }
            }

            public RedClassProperty(IRedClass cls, string i)
            {
                obj = cls;
                propertyName = i;
            }
        }

        private IRedType CalculatePropertyGridData()
        {
            try
            {
                IRedType data;
                if (Parent != null && Parent.Data is IRedArray ar)
                {
                    Type type = typeof(RedArrayItem<>).MakeGenericType(PropertyType);
                    var rai = (IRedType)System.Activator.CreateInstance(type, ar, ar.IndexOf(Data));
                    //rai.WhenAnyValue(x => x).Subscribe(x => IsDirty = true);
                    return rai;
                }
                if (Parent != null && Parent.Data is IRedClass cls && propertyName != null)
                {
                    Type type = typeof(RedClassProperty<>).MakeGenericType(PropertyType);
                    var rcp = (IRedType)System.Activator.CreateInstance(type, cls, propertyName);
                    //rcp.WhenAnyValue(x => x).Subscribe(x => IsDirty = true);
                    return rcp;
                }
                if (Data is IRedArray ary)
                {
                    var raw = new RedArrayWrapper(ary);
                    //raw.WhenAnyValue(x => x).Subscribe(x => IsDirty = true);
                    return raw;
                }
                if (PropertyType.IsAssignableTo(typeof(IRedClass)))
                {
                    data = Data;
                }
                else
                {
                    data = Parent?.Data ?? null;
                }

                if (data is IRedBaseHandle handle)
                {
                    //this.File.Chunks[handle.Pointer].IsHandled = true;
                    return handle.GetValue();
                }
                else
                {
                    return data;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return null;
            }
        }

        //private ObservableCollection<ChunkViewModel> _properties;

        private ObservableCollection<ChunkViewModel> CalculateProperties()
        {
            var properties = new ObservableCollection<ChunkViewModel>();
            try
            {
                var obj = Data;
                if (Data is IRedBaseHandle handle)
                {
                    //this.File.Chunks[handle.Pointer].IsHandled = true;
                    obj = handle.GetValue();
                }
                if (obj is IRedArray ary)
                {
                    for (int i = 0; i < ary.Count; i++)
                    {
                        properties.Add(new ChunkViewModel((IRedType)ary[i], this));
                    }
                }
                else if (obj is RedBaseClass redClass)
                {
                    var pis = GetTypeInfo(redClass.GetType()).PropertyInfos;
                    pis.Sort((a, b) => a.Name.CompareTo(b.Name));
                    pis.ForEach((pi) =>
                    {
                        IRedType value;
                        if (pi.RedName == null)
                        {
                            value = (IRedType)redClass.GetType().GetProperty(pi.Name).GetValue(redClass, null);
                        }
                        else
                        {
                            value = (IRedType)pi.GetValue(redClass);
                        }
                        properties.Add(new ChunkViewModel(value, this, pi.RedName));
                    });
                }
                else if (obj is SerializationDeferredDataBuffer sddb && sddb.Data is Package04 p4)
                {
                    var chunks = p4.Chunks;
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        properties.Add(new ChunkViewModel(chunks[i], this));
                    }
                }
                else if (obj is SharedDataBuffer sdb)
                {
                    if (sdb.Data is Package04 p42)
                    {
                        var chunks = p42.Chunks;
                        for (int i = 0; i < chunks.Count; i++)
                        {
                            properties.Add(new ChunkViewModel(chunks[i], this));
                        }
                    }
                    if (sdb.File is CR2WFile cr2)
                    {
                        //var chunks = cr2.Chunks;
                        //for (int i = 0; i < chunks.Count; i++)
                        //{
                        //    properties.Add(new ChunkViewModel(i, chunks[i], this));
                        //}
                        properties.Add(new ChunkViewModel(cr2.RootChunk, this));

                    }
                }
                else if (obj is DataBuffer db && db.Data is Package04 p43)
                {
                    var chunks = p43.Chunks;
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        properties.Add(new ChunkViewModel(chunks[i], this));
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                //throw;
            }
            return properties;
        }

        [Reactive] public bool IsSelected { get; set; }

        [Reactive] public bool IsDeleteReady { get; set; }

        [Reactive] public bool IsExpanded { get; set; }

        [Reactive] public bool IsHandled { get; set; }

        public string propertyName { get; }

        public string Name { get
            {
                if (propertyName != null)
                {
                    return propertyName;
                }
                if (IsInArray)
                {
                    return Parent.GetIndexOf(this).ToString();
                }
                return null;
            }
            set
            {

            }
        }

        private bool CalculateIsDefault()
        {
            if (Data == null)
                return true;
            if (Parent != null && propertyName != null && Data is not IRedBaseHandle)
            {
                Type parentType = Parent.PropertyType;
                if (Parent.Data is IRedBaseHandle handle && handle != null)
                {
                    parentType = handle.GetValue().GetType();
                }
                var epi = GetPropertyByRedName(parentType, propertyName);
                if (epi != null)
                {
                    return IsDefault(parentType, epi, Data);
                }
                return false;
            }
            return false;
        }

        public int GetIndexOf(ChunkViewModel child)
        {
            return Properties.IndexOf(child);
        }

        public int Level => Parent == null ? 0 : Parent.Level + 1;

        private Flags _flags;

        public Type PropertyType
        {
            get
            {
                var type = Data?.GetType() ?? null;
                if (Parent != null)
                {
                    var parent = Parent.Data;
                    Type parentType = Parent.PropertyType;
                    if (Parent.Data is IRedBaseHandle handle && handle != null)
                    {
                        parent = handle.GetValue();
                        parentType = handle.GetValue().GetType();
                    }
                    var propInfo = GetPropertyByRedName(parentType, propertyName) ?? null;
                    if (propInfo != null)
                    {
                        if (type == null || type == propInfo.Type)
                        {
                            _flags = propInfo.Flags;
                            type = propInfo.Type;
                        }
                    }
                }
                return type;
            }
        }

        public string Type
        {
            get
            {
                //if (PropertyType == typeof(IRedBaseHandle))
                //{
                //    var handle = (IRedBaseHandle)Data;
                //    return "Handle: " + (handle?.File?.Chunks[handle.Pointer]?.GetType().Name ?? "null");
                //}
                return PropertyType != null ? GetRedTypeFromCSType(PropertyType, _flags) : "null";
            }
        }

        public string EndType
        {
            get
            {
                if (Data is IRedBaseHandle handle)
                {
                    return GetTypeRedName(handle.InnerType);
                }
                return Type;
            }
        }

        public bool IsInArray => Parent != null && Parent.IsArray;

        public bool IsArray => PropertyType != null && (PropertyType.IsAssignableTo(typeof(IRedArray)) || PropertyType.IsAssignableTo(typeof(DataBuffer)) || PropertyType.IsAssignableTo(typeof(SharedDataBuffer)) || PropertyType.IsAssignableTo(typeof(SerializationDeferredDataBuffer)));

        public int ArrayIndexWidth { get
            {
                var width = 0;
                if (Parent != null)
                {
                    if (Parent.Properties.Count < 10)
                        width += 16;
                    else if (Parent.Properties.Count < 100)
                        width += 21;
                    else if (Parent.Properties.Count < 1000)
                        width += 26;
                    else
                        width += 31;
                }
                if (PropertyType?.IsAssignableTo(typeof(IRedArray)) ?? false)
                    width += 20;
                return width;
            }
        }

        public string XPath
        {
            get
            {
                if (Parent == null)
                {
                    return "root";
                }
                else
                {
                    var xpath = Parent.XPath;
                    if (IsInArray)
                        xpath += $"[{Name}]";
                    else if (Name != "")
                        xpath += "." + Name;
                    return xpath;
                }
            }
        }

        private string CalculateValue()
        {
            var str = new StringBuilder();
            if (Data == null)
            {
                return "null";
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedString)))
            {
                var value = (IRedString)Data;
                if (value.GetValue() == "")
                {
                    return "null";
                }
                else
                {
                    return value.GetValue();
                }
            }
            else if (PropertyType.IsAssignableTo(typeof(LocalizationString)))
            {
                var value = (LocalizationString)Data;
                if (value.Value == "")
                {
                    return "null";
                }
                else
                {
                    return value.Value;
                }
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedArray)))
            {
                var value = (IRedArray)Data;
                return $"{Type} [{value.Count}]";
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedBaseHandle)))
            {
                var value = (IRedBaseHandle)Data;
                str.Append(Type);
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedEnum)))
            {
                var value = (IRedEnum)Data;
                return value.ToEnumString();
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedBitField)))
            {
                var value = (IRedBitField)Data;
                return value.ToBitFieldString();
            }
            else if (PropertyType.IsAssignableTo(typeof(TweakDBID)))
            {
                var value = (TweakDBID)Data;
                return value.Value.ToString();
            }
            else if (PropertyType.IsAssignableTo(typeof(CBool)))
            {
                var value = (CBool)Data;
                return value ? "True" : "False";
            }
            else if (PropertyType.IsAssignableTo(typeof(CRUID)))
            {
                var value = (CRUID)Data;
                return ((ulong)value).ToString();
            }
            else if (PropertyType.IsAssignableTo(typeof(CUInt64)))
            {
                var value = (CUInt64)Data;
                return ((ulong)value).ToString();
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedInteger)))
            {
                var value = (IRedInteger)Data;
                return (value switch {
                    CUInt8 uint64 => (float)uint64,
                    CInt8 uint64 => (float)uint64,
                    CInt16 uint64 => (float)uint64,
                    CUInt16 uint64 => (float)uint64,
                    CInt32 uint64 => (float)uint64,
                    CUInt32 uint64 => (float)uint64,
                    CInt64 uint64 => (float)uint64,
                    _ => throw new ArgumentOutOfRangeException(nameof(value)),
                }).ToString();
            }
            else if (PropertyType.IsAssignableTo(typeof(FixedPoint)))
            {
                var value = (FixedPoint)Data;
                return ((float)value).ToString("R");
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedPrimitive<float>)))
            {
                var value = (IRedPrimitive)Data;
                return ((float)(CFloat)value).ToString("R");
            }
            else if (PropertyType.IsAssignableTo(typeof(IRedRef)))
            {
                var value = (IRedRef)Data;
                if (value != null && value.DepotPath != "")
                {
                    return value.DepotPath;
                }
                else
                {
                    return "null";
                }
            }
            else
            {
                str.Append(Type ?? "null");
            }

            if (propertyName == null)
            {
                // some common "names" of classes that might be useful to display in the UI
                var name = GetPropertyByName(PropertyType, "Name");
                var partName = GetPropertyByName(PropertyType, "PartName");
                var slotName = GetPropertyByName(PropertyType, "SlotName");
                if (name != null)
                {
                    str.Append($" ({name.GetValue((IRedClass)Data)})");
                }
                else if (partName != null)
                {
                    str.Append($" ({partName.GetValue((IRedClass)Data)})");
                }
                else if (slotName != null)
                {
                    str.Append($" ({slotName.GetValue((IRedClass)Data)})");
                }
            }
            return str.ToString();
        }

        public string Extension
        {
            get
            {
                if (PropertyType == null)
                {
                    return "Error";
                }
                if (PropertyType.IsAssignableTo(typeof(IRedInteger)))
                {
                    return "SymbolNumeric";
                }
                if (PropertyType.IsAssignableTo(typeof(IRedPrimitive<float>)))
                {
                    return "SymbolNumeric";
                }
                if (PropertyType.IsAssignableTo(typeof(IRedString)))
                {
                    return "SymbolString";
                }
                if (PropertyType.IsAssignableTo(typeof(IRedArray)))
                {
                    return "SymbolArray";
                }
                if (PropertyType.IsAssignableTo(typeof(IRedEnum)))
                {
                    return "SymbolEnum";
                }
                if (PropertyType.IsAssignableTo(typeof(IRedRef)))
                {
                    return "FileSymlinkFile";
                }
                if (PropertyType.IsAssignableTo(typeof(IRedBitField)))
                {
                    return "SymbolEnum";
                }
                if (PropertyType.IsAssignableTo(typeof(CBool)))
                {
                    return "SymbolBoolean";
                }
                if (PropertyType.IsAssignableTo(typeof(IRedBaseHandle)))
                {
                    return "References";
                }
                if (PropertyType.IsAssignableTo(typeof(DataBuffer)) || PropertyType.IsAssignableTo(typeof(SerializationDeferredDataBuffer)))
                {
                    return "GroupByRefType";
                }
                if (PropertyType.IsAssignableTo(typeof(CResourceAsyncReference<>)) || PropertyType.IsAssignableTo(typeof(CResourceReference<>)))
                {
                    return "RepoPull";
                }
                if (PropertyType.IsAssignableTo(typeof(IRedPrimitive)))
                {
                    return "DebugBreakpointDataUnverified";
                }
                if (PropertyType.IsAssignableTo(typeof(WorldTransform)))
                {
                    return "Compass";
                }
                if (PropertyType.IsAssignableTo(typeof(WorldPosition)))
                {
                    return "Move";
                }
                if (PropertyType.IsAssignableTo(typeof(Quaternion)))
                {
                    return "IssueReopened";
                }
                if (PropertyType.IsAssignableTo(typeof(CColor)))
                {
                    return "SymbolColor";
                }
                return "SymbolClass";
            }
        }

        #endregion Properties

        public bool CanBeDroppedOn(ChunkViewModel target)
        {
            return PropertyType == target.PropertyType;
        }

        public ICommand OpenRefCommand { get; private set; }
        private bool CanOpenRef() => Data is IRedRef r && r.DepotPath != null;
        private void ExecuteOpenRef()
        {
            if (Data is IRedRef r)
            {
                //string depotpath = r.DepotPath;
                //Tab.File.OpenRefAsTab(depotpath);
                Locator.Current.GetService<AppViewModel>().OpenFileFromDepotPath(r.DepotPath);
            }
            //var key = FNV1A64HashAlgorithm.HashString(depotpath);

            //var _gameControllerFactory = Locator.Current.GetService<IGameControllerFactory>();
            //var _archiveManager = Locator.Current.GetService<IArchiveManager>();

            //if (_archiveManager.Lookup(key).HasValue)
            //{
            //    _gameControllerFactory.GetController().AddToMod(key);
            //}
        }


        public ICommand AddItemToArrayCommand { get; private set; }
        private bool CanAddItemToArray() => Data is IRedArray;
        private void ExecuteAddItemToArray()
        {
            var type = (Data as IRedArray).InnerType;
            var newItem = RedTypeManager.CreateRedType(type);
            if (newItem is IRedBaseHandle handle)
            {
                var pointee = RedTypeManager.CreateRedType(handle.InnerType);
                handle.SetValue((IRedClass)pointee);
            }
            (Data as IRedArray).Add(newItem);
            Properties.Add(new ChunkViewModel(newItem, this));
            //this.RaisePropertyChanged("Data");
            IsExpanded = true;
            Tab.File.SetIsDirty(true);
        }

        public ICommand AddItemToCompiledDataCommand { get; private set; }
        private bool CanAddItemToCompiledData() => Data is DataBuffer;
        private void ExecuteAddItemToCompiledData()
        {
            var db = Data as DataBuffer;
            ObservableCollection<string> existing = null;
            if (db.Data is Package04 pkg)
            {
                existing = new ObservableCollection<string>(pkg.Chunks.Select(t => t.GetType().Name).Distinct());
            }
            var app = Locator.Current.GetService<AppViewModel>();
            app.SetActiveDialog(new AddChunkDialogViewModel()
            {
                DialogHandler = HandleChunk,
                ExistingClasses = existing
            });
        }
        public void HandleChunk(DialogViewModel sender)
        {
            var app = Locator.Current.GetService<AppViewModel>();
            app.CloseDialogCommand.Execute(null);
            if (sender != null && Data is DataBuffer db && db.Data is Package04 pkg)
            {
                var vm = sender as AddChunkDialogViewModel;
                var instance = RedTypeManager.Create(vm.SelectedClass);
                AddChunkToDataBuffer(instance, Properties.Count);
            }
        }
        public void AddChunkToDataBuffer(IRedClass instance, int index)
        {
            if (Data is DataBuffer db && db.Data is Package04 pkg)
            {
                //pkg.Chunks.Add(instance);
                pkg.Chunks.Insert(index, instance);
                //_properties.Add(new ChunkViewModel(instance, this));
                //_properties.Insert(index, new ChunkViewModel(instance, this));
                foreach (var prop in Properties)
                    prop.RaisePropertyChanged("Name");
                this.RaisePropertyChanged("Data");
                IsExpanded = true;
                Tab.File.SetIsDirty(true);
            }
        }


        public ICommand DeleteItemCommand { get; private set; }
        private bool CanDeleteItem() => IsInArray;
        private void ExecuteDeleteItem()
        {
            if (Parent.Data is IRedArray ary)
            {
                //IsSelected = false;
                Parent.IsSelected = true;
                ary.Remove(Data);
                Parent.Properties.Remove(this);
                Tab.File.SetIsDirty(true);
            }
            if (Parent.Data is DataBuffer db && db.Data is Package04 pkg)
            {
                Parent.IsSelected = true;
                pkg.Chunks.Remove((IRedClass)Data);
                Parent.Properties.Remove(this);
                foreach (var prop in Parent.Properties)
                    prop.RaisePropertyChanged("Name");
                Tab.File.SetIsDirty(true);
            }
        }

        public ICommand ExportChunkCommand { get; private set; }
        private bool CanExportChunk() => Properties.Count > 0;
        private void ExecuteExportChunk()
        {
            Stream myStream;
            var saveFileDialog = new SaveFileDialog();

            saveFileDialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
            saveFileDialog.FilterIndex = 2;
            saveFileDialog.FileName = Type + ".json";
            saveFileDialog.RestoreDirectory = true;

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                if ((myStream = saveFileDialog.OpenFile()) != null)
                {
                    var dto = new RedClassDto(PropertyGridData, new
                    {
                        WolvenKitVersion = "8.4.0",
                        WKitJsonVersion = "0.0.1",
                        Exported = DateTime.UtcNow.ToString("o")
                    });
                    var json = JsonConvert.SerializeObject(dto, Formatting.Indented);

                    if (string.IsNullOrEmpty(json))
                    {
                        throw new SerializationException();
                    }

                    myStream.Write(json.ToCharArray().Select(c => (byte)c).ToArray());
                    myStream.Close();
                }
            }
        }

        public ICommand OpenChunkCommand { get; private set; }
        private bool CanOpenChunk() => Data is IRedClass && Parent != null;
        private void ExecuteOpenChunk()
        {
            if (Data is IRedClass cls)
                Tab.File.TabItemViewModels.Add(new RDTDataViewModel(cls, Tab.File));
        }
    }
}
