using System;
using System.Collections.Concurrent;
using System.Text;

namespace RemnantESP
{
    public class Engine
    {
        public static Engine Instance;
        public static UInt64 GNames;
        public static UInt64 GObjects;
        public static UInt64 GWorld;

        public static Memory Memory;

        public Engine(Memory mem) { Memory = mem; Instance = this; }
        public void UpdateAddresses()
        {
            // https://github.com/EpicGames/UnrealEngine/blob/4.22.3-release/Engine/Source/Runtime/Core/Private/UObject/UnrealNames.cpp#L888 Hardcoded name '%s'
            var GNamesPattern = (UInt64)Memory.FindPattern("48 8B 05 ? ? ? ? 48 85 C0 75 5F");
            // https://github.com/EpicGames/UnrealEngine/blob/4.22.3-release/Engine/Source/Runtime/Launch/Private/LaunchEngineLoop.cpp#L3905 r.OneFrameThreadLag
            var GWorldPattern = (UInt64)Memory.FindPattern("48 8B 1D ? ? ? ? 48 85 DB 74 3B 41 B0 01");
            // var obj = (gobject + 8i64 * (index / 0x10000)), inlined/appears multiple
            var GObjectsPattern = (UInt64)Memory.FindPattern("C1 F9 10 48 63 C9 48 8D 14 40 48 8B 05");

            var offset = Memory.ReadProcessMemory<UInt32>(GNamesPattern + 3);
            GNames = Memory.ReadProcessMemory<UInt64>(GNamesPattern + offset + 7);

            offset = Memory.ReadProcessMemory<UInt32>(GObjectsPattern + 13);
            GObjects = GObjectsPattern + offset + 17 - Memory.BaseAddress;

            offset = Engine.Memory.ReadProcessMemory<UInt32>(GWorldPattern + 3);
            GWorld = Memory.ReadProcessMemory<UInt64>(GWorldPattern + offset + 7);

            // DumpClasses();
            //DumpGNames();
        }
        public String GetName(Int32 i)
        {
            var fNamePtr = Memory.ReadProcessMemory<ulong>(GNames + ((UInt64)i / 0x4000) * 8);
            var fName2 = Memory.ReadProcessMemory<ulong>(fNamePtr + (8 * ((UInt64)i % 0x4000)));
            var fName3 = Memory.ReadProcessMemory<String>(fName2 + 0xC);
            if (fName3.Contains("/")) return fName3.Substring(fName3.LastIndexOf("/") + 1);
            return fName3;
        }
        ConcurrentDictionary<UInt64, String> AddrToClass = new ConcurrentDictionary<UInt64, String>();
        public String GetFullName(UInt64 entityAddr)
        {
            if (AddrToClass.ContainsKey(entityAddr)) return AddrToClass[entityAddr];
            var classPtr = Memory.ReadProcessMemory<UInt64>(entityAddr + 0x10);
            var classNameIndex = Memory.ReadProcessMemory<Int32>(classPtr + 0x18);
            var name = GetName(classNameIndex);
            UInt64 outerEntityAddr = entityAddr;
            var parentName = "";
            while ((outerEntityAddr = Memory.ReadProcessMemory<UInt64>(outerEntityAddr + 0x20)) > 0)
            {
                var outerNameIndex = Memory.ReadProcessMemory<Int32>(outerEntityAddr + 0x18);
                var tempName = GetName(outerNameIndex);
                if (tempName == "") break;
                if (tempName == "None") break;
                parentName = tempName + "." + parentName;
            }
            name += " " + parentName;
            var nameIndex = Memory.ReadProcessMemory<Int32>(entityAddr + 0x18);
            name += GetName(nameIndex);
            AddrToClass[entityAddr] = name;
            return name;
        }
        ConcurrentDictionary<String, Boolean> ClassIsSubClass = new ConcurrentDictionary<String, Boolean>();
        public Boolean IsA(UInt64 entityClassAddr, UInt64 targetClassAddr)
        {
            var key = entityClassAddr + ":" + targetClassAddr;
            if (ClassIsSubClass.ContainsKey(key)) return ClassIsSubClass[key];
            if (entityClassAddr == targetClassAddr) return true;
            while (true)
            {
                var tempEntityClassAddr = Memory.ReadProcessMemory<UInt64>(entityClassAddr + 0x40);
                if (entityClassAddr == tempEntityClassAddr || tempEntityClassAddr == 0)
                    break;
                entityClassAddr = tempEntityClassAddr;
                if (entityClassAddr == targetClassAddr)
                {
                    ClassIsSubClass[key] = true;
                    return true;
                }
            }
            ClassIsSubClass[key] = false;
            return false;
        }
        ConcurrentDictionary<String, UInt64> ClassToAddr = new ConcurrentDictionary<String, UInt64>();
        public UInt64 FindClass(String className)
        {
            if (ClassToAddr.ContainsKey(className)) return ClassToAddr[className];
            var masterEntityList = Memory.ReadProcessMemory<UInt64>(Memory.BaseAddress + GObjects);
            var num = Memory.ReadProcessMemory<UInt64>(Memory.BaseAddress + GObjects + 0x14);
            var entityChunk = 0u;
            var entityList = Memory.ReadProcessMemory<UInt64>(masterEntityList);
            for (var i = 0u; i < num; i++)
            {
                if ((i / 0x10000) != entityChunk)
                {
                    entityChunk = (UInt32)(i / 0x10000);
                    entityList = Memory.ReadProcessMemory<UInt64>(masterEntityList + 8 * entityChunk);
                }
                var entityAddr = Memory.ReadProcessMemory<UInt64>(entityList + 24 * (i % 0x10000));
                if (entityAddr == 0) continue;
                var name = GetFullName(entityAddr);
                if (name == className)
                {
                    ClassToAddr[className] = entityAddr;
                    return entityAddr;
                }
            }
            return 0;
        }
        public Int32 FieldIsClass(String className, String fieldName)
        {
            var classAddr = FindClass(className);
            var fieldAddr = GetFieldAddr(classAddr, classAddr, fieldName);
            var offset = GetFieldOffset(fieldAddr);
            return offset;
        }
        ConcurrentDictionary<UInt64, ConcurrentDictionary<String, UInt64>> ClassFieldToAddr = new ConcurrentDictionary<UInt64, ConcurrentDictionary<String, UInt64>>();
        public UInt64 GetFieldAddr(UInt64 origClassAddr, UInt64 classAddr, String fieldName)
        {
            if (ClassFieldToAddr.ContainsKey(origClassAddr) && ClassFieldToAddr[origClassAddr].ContainsKey(fieldName)) return ClassFieldToAddr[origClassAddr][fieldName];
            var field = classAddr + 0x40;
            while ((field = Memory.ReadProcessMemory<UInt64>(field + 0x28)) > 0)
            {
                var fName = GetName(Memory.ReadProcessMemory<Int32>(field + 0x18));
                if (fName == fieldName)
                {
                    //var offset = Memory.ReadProcessMemory<Int32>(field + 0x44);
                    if (!ClassFieldToAddr.ContainsKey(origClassAddr))
                        ClassFieldToAddr[origClassAddr] = new ConcurrentDictionary<String, UInt64>();
                    ClassFieldToAddr[origClassAddr][fieldName] = field;
                    return field;
                }
            }
            var parentClass = Memory.ReadProcessMemory<UInt64>(classAddr + 0x40);
            var c = GetFullName(classAddr);
            var pc = GetFullName(parentClass);
            if (parentClass == 0) throw new Exception("bad field");
            return GetFieldAddr(origClassAddr, parentClass, fieldName);
        }
        ConcurrentDictionary<UInt64, Int32> FieldAddrToOffset = new ConcurrentDictionary<UInt64, Int32>();
        public Int32 GetFieldOffset(UInt64 fieldAddr)
        {
            if (FieldAddrToOffset.ContainsKey(fieldAddr)) return FieldAddrToOffset[fieldAddr];
            var offset = Memory.ReadProcessMemory<Int32>(fieldAddr + 0x44);
            FieldAddrToOffset[fieldAddr] = offset;
            return offset;
        }
        ConcurrentDictionary<UInt64, String> FieldAddrToType = new ConcurrentDictionary<UInt64, String>();
        public String GetFieldType(UInt64 fieldAddr)
        {
            if (FieldAddrToType.ContainsKey(fieldAddr)) return FieldAddrToType[fieldAddr];
            var fieldType = Memory.ReadProcessMemory<UInt64>(fieldAddr + 0x10);
            var name = GetName(Memory.ReadProcessMemory<Int32>(fieldType + 0x18));
            FieldAddrToType[fieldAddr] = name;
            return name;
        }
        public String DumpClass(String className)
        {
            var classAddr = FindClass(className);
            return DumpClass(classAddr);
        }
        public String DumpClass(UInt64 classAddr)
        {
            var sb = new StringBuilder();
            var name = GetFullName(classAddr);
            sb.Append(classAddr.ToString("X") + " : " + name);
            var pcAddr = classAddr;
            var c = 0;
            while((pcAddr = Memory.ReadProcessMemory<UInt64>(pcAddr + 0x40)) > 0 && c++ < 20){
                var super = GetFullName(pcAddr);
                sb.Append(" : " + super);
            }
            sb.AppendLine();

            var field = classAddr + 0x40;
            while (true)
            {
                var nextField = Memory.ReadProcessMemory<UInt64>(field + 0x28);
                if (nextField == field) break;
                field = nextField;
                if (field == 0) break;
                var fieldName = GetFullName(field);
                var f = Memory.ReadProcessMemory<UInt64>(field + 0x70);
                var fType = GetName(Memory.ReadProcessMemory<Int32>(f + 0x18));
                var fName = GetName(Memory.ReadProcessMemory<Int32>(field + 0x18));
                var offset = Memory.ReadProcessMemory<Int32>(field + 0x44);
                if (fType == "None" && String.IsNullOrEmpty(fName)) break;
                sb.AppendLine("  " + fType + " " + fName + " : 0x" + offset.ToString("X"));
            }
            return sb.ToString();
        }
        public void DumpClasses()
        {
            var masterEntityList = Memory.ReadProcessMemory<UInt64>(Memory.BaseAddress + GObjects);
            var num = Memory.ReadProcessMemory<UInt32>(Memory.BaseAddress + GObjects + 0x14);
            var entityChunk = 0u;
            var entityList = Memory.ReadProcessMemory<UInt64>(masterEntityList);
            var sb = new StringBuilder();
            for (var i = 0u; i < num; i++)
            {
                if ((i / 0x10000) != entityChunk)
                {
                    entityChunk = (UInt32)(i / 0x10000);
                    entityList = Memory.ReadProcessMemory<UInt64>(masterEntityList + 8 * entityChunk);
                }
                var entityAddr = Memory.ReadProcessMemory<UInt64>(entityList + 24 * (i % 0x10000));
                if (entityAddr == 0) continue;
                sb.AppendLine(DumpClass(entityAddr).ToString());
            }
            System.IO.File.WriteAllText("classes.txt", sb.ToString());
        }
        public void DumpGNames()
        {
            var count = 0x100000;
            var sb = new StringBuilder();
            for (var i = 0u; i < count; i++)
            {
                var name = GetName((Int32)i);
                var fNamePtr = Memory.ReadProcessMemory<ulong>(GNames + (i / 0x4000) * 8);
                var fName2 = Memory.ReadProcessMemory<ulong>(fNamePtr + (8 * (i % 0x4000)));
                var fNameI = Memory.ReadProcessMemory<Int32>(fName2);
                var fName3 = Memory.ReadProcessMemory<String>(fName2 + 0xc);
                sb.AppendLine(fNameI.ToString("X") + " : " + fName3 + " (" + i.ToString("X") + ")");
                //System.IO.File.AppendAllText("gnames.txt", fNameI.ToString("X") + " : " + fName3 + " (" + i.ToString("X") + ")\n");
                //Console.WriteLine(fNameI.ToString("X") + " : " + fName3 + " (" + i + ")");
            }
            System.IO.File.WriteAllText("gname.txt", sb.ToString());
        }
        public void DumpGObjects()
        {
            var sb = new StringBuilder();
            var entityList = Memory.ReadProcessMemory<UInt64>(Memory.BaseAddress + GObjects);
            var count = Memory.ReadProcessMemory<UInt64>(Memory.BaseAddress + GObjects + 0x14);
            entityList = Memory.ReadProcessMemory<UInt64>(entityList);
            for (var i = 0u; i < count; i++)
            {
                var entityAddr = Memory.ReadProcessMemory<UInt64>((entityList + 8 * (i / 66560)) + 24 * (i % 66560));
                if (entityAddr == 0) continue;
                var name = GetFullName(entityAddr);
                sb.AppendLine(i + " : " + entityAddr.ToString("X") + " : " + name);
            }
            System.IO.File.WriteAllText("gobjects.txt", sb.ToString());
        }
        public void DumpActors()
        {
            var sb = new System.Text.StringBuilder();

            var World = new UEObject(Engine.GWorld);
            var Levels = World["Levels"];
            for (var levelIndex = 0u; levelIndex < Levels.Num; levelIndex++)
            {
                var Level = Levels[levelIndex];
                var Actors = new Engine.UEObject(Level.Address + 0xA8);
                for (var i = 0u; i < Actors.Num; i++)
                {
                    var Actor = Actors[i];
                    var debug = GetFullName(Actor.Address) + " || " + Actor.ClassName + " || " + DumpClass(Actor.ClassAddr);
                    sb.AppendLine(i + " : " + debug);

                }
            }
            System.IO.File.WriteAllText("actors.txt", sb.ToString());
        }
        public class UEObject
        {
            String _className;
            public String ClassName
            {
                get
                {
                    if (_className != null) return _className;
                    _className = Engine.Instance.GetFullName(ClassAddr);
                    return _className;
                }
            }
            static ConcurrentDictionary<UInt64, UInt64> ObjToClass = new ConcurrentDictionary<UInt64, UInt64>();
            UInt64 _classAddr = UInt64.MaxValue;
            public UInt64 ClassAddr
            {
                get
                {
                    if (_classAddr != UInt64.MaxValue) return _classAddr;
                    if (ObjToClass.ContainsKey(Address))
                    {
                       // _classAddr = ObjToClass[Address];
                       // return _classAddr;
                    }
                    _classAddr = Engine.Memory.ReadProcessMemory<UInt64>(Address + 0x10);
                    //ObjToClass[Address] = _classAddr;
                    return _classAddr;
                }
            }
            public UEObject(UInt64 address)
            {
                Address = address;
            }
            public Boolean IsA(String className)
            {
                return Engine.Instance.IsA(ClassAddr, Engine.Instance.FindClass(className));
            }
            public UInt32 FieldOffset;
            public Byte[] Data;
            public UInt64 _value = UInt64.MaxValue;
            public UInt64 Value
            {
                get
                {
                    if (_value != UInt64.MaxValue) return _value;
                    _value = Engine.Memory.ReadProcessMemory<UInt64>(Address);
                    return _value;
                }
            }
            public UInt64 Address;
            public UEObject this[String key]
            {
                get
                {
                    var fieldAddr = Engine.Instance.GetFieldAddr(ClassAddr, ClassAddr, key);
                    var fieldType = Engine.Instance.GetFieldType(fieldAddr);
                    var offset = (UInt32)Engine.Instance.GetFieldOffset(fieldAddr);
                    UEObject obj;
                    if (fieldType == "ObjectProperty" || fieldType == "ScriptStruct")
                        obj = new UEObject(Engine.Memory.ReadProcessMemory<UInt64>(Address + offset)) { FieldOffset = offset };
                    else if (fieldType == "ArrayProperty")
                    {
                        obj = new UEObject(Address + offset);
                        obj._classAddr = Engine.Memory.ReadProcessMemory<UInt64>(fieldAddr + 0x10);
                    }
                    else if (fieldType.Contains("Bool"))
                    {
                        obj = new UEObject(Address + offset);
                        obj._classAddr = Engine.Memory.ReadProcessMemory<UInt64>(fieldAddr + 0x10);
                        var boolMask = Engine.Memory.ReadProcessMemory<UInt64>(fieldAddr + 0x70);
                        boolMask = (boolMask >> 16) & 0xff;
                        var fullVal = Engine.Memory.ReadProcessMemory<Byte>(Address + offset);
                        obj._value = ((fullVal & boolMask) == boolMask) ? 1u : 0;
                    }
                    else
                    {
                        obj = new UEObject(Address + offset);
                        obj._classAddr = Engine.Memory.ReadProcessMemory<UInt64>(fieldAddr + 0x70);
                    }
                    if (obj.Address == 0)
                    {
                        return null;
                        //var classInfo = Engine.Instance.DumpClass(ClassAddr);
                        //throw new Exception("bad addr");
                    }
                    return obj;
                }
            }
            public UInt32 Num { get { return Engine.Memory.ReadProcessMemory<UInt32>(Address + 8); } }
            public UEObject this[UInt32 index]
            {
                get
                {
                    return new UEObject(Engine.Memory.ReadProcessMemory<UInt64>(Engine.Memory.ReadProcessMemory<UInt64>(Address) + index * 8));
                }
            }
        }
    }
}
