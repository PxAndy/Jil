﻿using Sigil.NonGeneric;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Jil.Serialize
{
    class InlineSerializer
    {
        public static bool ReorderMembers = true;

        static Dictionary<char, string> CharacterEscapes = 
            new Dictionary<char, string>{
                { '\\',  @"\\" },
                { '"', @"\""" },
                { '\u0000', @"\u0000" },
                { '\u0001', @"\u0001" },
                { '\u0002', @"\u0002" },
                { '\u0003', @"\u0003" },
                { '\u0004', @"\u0004" },
                { '\u0005', @"\u0005" },
                { '\u0006', @"\u0006" },
                { '\u0007', @"\u0007" },
                { '\u0008', @"\u0008" },
                { '\u0009', @"\u0009" },
                { '\u000A', @"\u000A" },
                { '\u000B', @"\u000B" },
                { '\u000C', @"\u000C" },
                { '\u000D', @"\u000D" },
                { '\u000E', @"\u000E" },
                { '\u000F', @"\u000F" },
                { '\u0010', @"\u0010" },
                { '\u0011', @"\u0011" },
                { '\u0012', @"\u0012" },
                { '\u0013', @"\u0013" },
                { '\u0014', @"\u0014" },
                { '\u0015', @"\u0015" },
                { '\u0016', @"\u0016" },
                { '\u0017', @"\u0017" },
                { '\u0018', @"\u0018" },
                { '\u0019', @"\u0019" },
                { '\u001A', @"\u001A" },
                { '\u001B', @"\u001B" },
                { '\u001C', @"\u001C" },
                { '\u001D', @"\u001D" },
                { '\u001E', @"\u001E" },
                { '\u001F', @"\u001F" }
        };

        static Action<TextWriter, ForType> LookupPrimitiveType<ForType>()
        {
            var forType = typeof(ForType);

            if (forType == typeof(byte))
            {
                Action<TextWriter, byte> ret = (writer, b) => { writer.Write(b); };

                return (Action<TextWriter, ForType>)(object)ret;
            }

            if (forType == typeof(sbyte))
            {
                Action<TextWriter, sbyte> ret = (writer, s) => { writer.Write(s); };

                return (Action<TextWriter, ForType>)(object)ret;
            }

            if (forType == typeof(short))
            {
                Action<TextWriter, short> ret = (writer, s) => { writer.Write(s); };

                return (Action<TextWriter, ForType>)(object)ret;
            }

            if (forType == typeof(ushort))
            {
                Action<TextWriter, ushort> ret = (writer, u) => { writer.Write(u); };

                return (Action<TextWriter, ForType>)(object)ret;
            }

            if (forType == typeof(int))
            {
                Action<TextWriter, int> ret = (writer, i) => { writer.Write(i); };

                return (Action<TextWriter, ForType>)(object)ret;
            }

            if (forType == typeof(uint))
            {
                Action<TextWriter, uint> ret = (writer, u) => { writer.Write(u); };

                return (Action<TextWriter, ForType>)(object)ret;
            }

            if (forType == typeof(long))
            {
                Action<TextWriter, long> ret = (writer, l) => { writer.Write(l); };

                return (Action<TextWriter, ForType>)(object)ret;
            }

            if (forType == typeof(ulong))
            {
                Action<TextWriter, ulong> ret = (writer, u) => { writer.Write(u); };

                return (Action<TextWriter, ForType>)(object)ret;
            }

            if (forType == typeof(float))
            {
                Action<TextWriter, float> ret = (writer, f) => { writer.Write(f); };

                return (Action<TextWriter, ForType>)(object)ret;
            }

            if (forType == typeof(double))
            {
                Action<TextWriter, double> ret = (writer, d) => { writer.Write(d); };

                return (Action<TextWriter, ForType>)(object)ret;
            }

            if (forType == typeof(decimal))
            {
                Action<TextWriter, decimal> ret = (writer, d) => { writer.Write(d); };

                return (Action<TextWriter, ForType>)(object)ret;
            }

            if (forType == typeof(string))
            {
                Action<TextWriter, string> ret = (writer, s) => { writer.Write("\""); writer.Write(s.JsonEscape()); writer.Write("\""); };

                return (Action<TextWriter, ForType>)(object)ret;
            }

            if (forType == typeof(char))
            {
                Action<TextWriter, char> ret = (writer, c) => { writer.Write("\""); writer.Write(c.JsonEscape()); writer.Write("\""); };

                return (Action<TextWriter, ForType>)(object)ret;
            }

            throw new NotImplementedException();
        }

        static MethodInfo TextWriter_WriteString = typeof(TextWriter).GetMethod("Write", new[] { typeof(string) });
        static void WriteString(string str, Emit emit)
        {
            emit.LoadArgument(0);
            emit.LoadConstant(str);
            emit.CallVirtual(TextWriter_WriteString);
        }

        static List<MemberInfo> OrderMembersForAccess(Type forType)
        {
            var fields = Utils.FieldOffsetsInMemory(forType);
            var props = Utils.PropertyFieldUsage(forType);

            var members = forType.GetProperties().Where(p => p.GetMethod != null).Cast<MemberInfo>().Concat(forType.GetFields());

            // This order appears to be the "best" for access speed purposes
            var ret =
                !ReorderMembers ?
                    members :
                    members.OrderBy(
                         m =>
                         {
                             var asField = m as FieldInfo;
                             if (asField != null)
                             {
                                 return fields[asField];
                             }

                             var asProp = m as PropertyInfo;
                             if (asProp != null)
                             {
                                 List<FieldInfo> usesFields;
                                 if (!props.TryGetValue(asProp, out usesFields))
                                 {
                                     return int.MaxValue;
                                 }

                                 return usesFields.Select(f => fields[f]).Min();
                             }

                             return int.MaxValue;
                         }
                    ).ThenBy(
                        m => m is FieldInfo ? 0 : 1
                    ).Reverse();

            return ret.ToList();
        }

        static HashSet<Type> FindRecursiveTypes(Type forType)
        {
            var alreadySeen = new HashSet<Type>();
            var ret = new HashSet<Type>();

            var pending = new List<Type>();
            pending.Add(forType);

            while (pending.Count > 0)
            {
                var curType = pending[0];
                pending.RemoveAt(0);

                if (curType.IsPrimitiveType()) continue;

                if (curType.IsListType())
                {
                    var listI = curType.GetListInterface();
                    var valType = listI.GetGenericArguments()[0];
                    pending.Add(valType);
                    continue;
                }

                if (curType.IsDictionaryType())
                {
                    var dictI = curType.GetDictionaryInterface();
                    var valType = dictI.GetGenericArguments()[1];
                    pending.Add(valType);
                    continue;
                }

                if (alreadySeen.Contains(curType))
                {
                    ret.Add(curType);
                    continue;
                }

                alreadySeen.Add(curType);

                foreach (var field in curType.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    pending.Add(field.FieldType);
                }

                foreach (var prop in curType.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetMethod != null))
                {
                    pending.Add(prop.PropertyType);
                }
            }

            return ret;
        }

        static void WriteMember(MemberInfo member, Emit emit, Dictionary<Type, Sigil.Local> recursiveTypes, Sigil.Local inLocal = null)
        {
            var asField = member as FieldInfo;
            var asProp = member as PropertyInfo;

            if (asField == null && asProp == null) throw new Exception("Wha?");

            var serializingType = asField != null ? asField.FieldType : asProp.PropertyType;

            // It's a list, go and build that code
            if (serializingType.IsListType())
            {
                if (inLocal != null)
                {
                    emit.LoadLocal(inLocal);
                }
                else
                {
                    emit.LoadArgument(1);
                }

                if (asField != null)
                {
                    emit.LoadField(asField);
                }

                if (asProp != null)
                {
                    var getMtd = asProp.GetMethod;
                    if (getMtd.IsVirtual)
                    {
                        emit.CallVirtual(getMtd);
                    }
                    else
                    {
                        emit.Call(getMtd);
                    }
                }

                using (var loc = emit.DeclareLocal(serializingType))
                {
                    emit.StoreLocal(loc);

                    if (serializingType.IsListType())
                    {
                        WriteList(serializingType, emit, recursiveTypes, loc);
                        return;
                    }

                    if (serializingType.IsDictionaryType())
                    {
                        WriteDictionary(serializingType, emit, recursiveTypes, loc);
                        return;
                    }

                    WriteList(serializingType, emit, recursiveTypes, loc);
                }

                return;
            }

            var isRecursive = recursiveTypes.ContainsKey(serializingType);

            if (isRecursive)
            {
                emit.LoadLocal(recursiveTypes[serializingType]);    // Action<TextWriter, serializingType>
            }

            // Only put this on the stack if we'll need it
            if (serializingType.IsPrimitiveType() || isRecursive)
            {
                emit.LoadArgument(0);   // TextWriter
            }

            if (inLocal == null)
            {
                emit.LoadArgument(1);       // TextWriter obj
            }
            else
            {
                emit.LoadLocal(inLocal);    // TextWriter obj
            }

            if (asField != null)
            {
                emit.LoadField(asField);    // TextWriter field
            }

            if (asProp != null)
            {
                var getMtd = asProp.GetMethod;
                if(getMtd.IsVirtual)
                {
                    emit.CallVirtual(getMtd);   // TextWriter prop
                }
                else
                {
                    emit.Call(getMtd);          // TextWriter prop
                }
            }

            if (isRecursive)
            {
                // Stack is:
                //  - serializingType
                //  - TextWriter
                //  - Action<TextWriter, serializingType>

                var recursiveAct = typeof(Action<,>).MakeGenericType(typeof(TextWriter), serializingType);
                var invoke = recursiveAct.GetMethod("Invoke");

                emit.Call(invoke);

                return;
            }

            if (serializingType.IsPrimitiveType())
            {
                WritePrimitive(serializingType, emit);
                return;
            }

            using (var loc = emit.DeclareLocal(serializingType))
            {
                emit.StoreLocal(loc);   // TextWriter;

                WriteObject(serializingType, emit, recursiveTypes, loc);
            }
        }

        static void WritePrimitive(Type primitiveType, Emit emit)
        {
            if (primitiveType == typeof(char) || primitiveType == typeof(string))
            {
                WriteEncodedString(primitiveType, emit);

                return;
            }

            var needsIntCoersion = primitiveType == typeof(byte) || primitiveType == typeof(sbyte) || primitiveType == typeof(short) || primitiveType == typeof(ushort);

            if (needsIntCoersion)
            {
                emit.Convert<int>();            // TextWriter int
                primitiveType = typeof(int);
            }

            var builtInMtd = typeof(TextWriter).GetMethod("Write", new[] { primitiveType });

            emit.CallVirtual(builtInMtd);       // --empty--
        }

        static void WriteEncodedString(Type stringType, Emit emit)
        {
            // top of stack is:
            //  - value
            //  - TextWriter

            if (stringType == typeof(char))
            {
                var writeChar = typeof(TextWriter).GetMethod("Write", new [] { typeof(char) });

                var lowestCharNeedingEncoding = (int)CharacterEscapes.Keys.OrderBy(c => (int)c).First();

                var needLabels = CharacterEscapes.OrderBy(kv => kv.Key).Select(kv => Tuple.Create(kv.Key - lowestCharNeedingEncoding, kv.Value)).ToList();

                var labels = new List<Tuple<Sigil.Label, string>>();

                int? prev = null;
                foreach(var pair in needLabels)
                {
                    if(prev != null && pair.Item1 - prev != 1) break;

                    var label = emit.DefineLabel();

                    labels.Add(Tuple.Create(label, pair.Item2));
                }

                var done = emit.DefineLabel();
                var slash = emit.DefineLabel();
                var quote = emit.DefineLabel();

                emit.Duplicate();                               // TextWriter char char
                emit.Convert<int>();
                emit.LoadConstant(lowestCharNeedingEncoding);   // TextWriter char char int
                emit.Subtract();                                // TextWriter char int

                emit.Switch(labels.Select(s => s.Item1).ToArray()); // TextWriter char

                // this is the fall-through (default) case

                emit.Duplicate();               // TextWriter char char
                emit.LoadConstant('\\');        // TextWriter char char \
                emit.BranchIfEqual(slash);      // TextWriter char

                emit.Duplicate();               // TextWriter char char
                emit.LoadConstant('"');         // TextWriter char char "
                emit.BranchIfEqual(quote);      // TextWriter clear

                emit.CallVirtual(writeChar);    // --empty--
                emit.Branch(done);              // --empty--

                emit.MarkLabel(slash);          // TextWriter char
                emit.Pop();                     // TextWriter
                emit.Pop();                     // --empty--
                WriteString(@"\\", emit);       // --empty--
                emit.Branch(done);              // --empty--

                emit.MarkLabel(quote);
                emit.Pop();                     // TextWriter
                emit.Pop();                     // --empty--
                WriteString(@"\""", emit);      // --empty--
                emit.Branch(done);              // --empty--

                foreach (var label in labels)
                {
                    emit.MarkLabel(label.Item1);    // TextWriter char
                    emit.Pop();                     // TextWriter
                    emit.Pop();                     // --empty--
                    WriteString(label.Item2, emit); // --empty-- 
                    emit.Branch(done);              // --empty--
                }

                emit.MarkLabel(done);

                return;
            }

            var writeString = typeof(TextWriter).GetMethod("Write", new [] { typeof(string) });

            emit.CallVirtual(writeString);
        }

        internal static void WriteObject(Type forType, Emit emit, Dictionary<Type, Sigil.Local> recursiveTypes, Sigil.Local inLocal = null)
        {
            var writeOrder = OrderMembersForAccess(forType);

            var notNull = emit.DefineLabel();

            if (inLocal != null)
            {
                emit.LoadLocal(inLocal);
            }
            else
            {
                emit.LoadArgument(1);
            }

            var end = emit.DefineLabel();

            emit.BranchIfTrue(notNull);
            WriteString("null", emit);
            emit.Branch(end);

            emit.MarkLabel(notNull);
            WriteString("{", emit);

            var firstPass = true;
            var previousMemberWasStringy = false;
            foreach (var member in writeOrder)
            {
                string keyString;
                if (firstPass)
                {
                    keyString = "\"" + member.Name.JsonEscape() + "\":";
                    firstPass = false;
                }
                else
                {
                    keyString = ",\"" + member.Name.JsonEscape() + "\":";
                }

                if (previousMemberWasStringy)
                {
                    keyString = "\"" + keyString;
                }

                var isStringy = member.IsStringyType();

                if (isStringy)
                {
                    keyString = keyString + "\"";
                }

                WriteString(keyString, emit);
                WriteMember(member, emit, recursiveTypes, inLocal);

                previousMemberWasStringy = isStringy;
            }

            if (previousMemberWasStringy)
            {
                WriteString("\"}", emit);
            }
            else
            {
                WriteString("}", emit);
            }

            emit.MarkLabel(end);
        }

        static void WriteList(Type listType, Emit emit, Dictionary<Type, Sigil.Local> recursiveTypes, Sigil.Local inLocal = null)
        {
            var elementType = listType.GetListInterface().GetGenericArguments()[0];

            var iEnumerable = typeof(IEnumerable<>).MakeGenericType(elementType);
            var iEnumerableGetEnumerator = iEnumerable.GetMethod("GetEnumerator");
            var enumeratorMoveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext");
            var enumeratorCurrent = iEnumerableGetEnumerator.ReturnType.GetProperty("Current").GetMethod;

            var iList = typeof(IList<>).MakeGenericType(elementType);

            var isRecursive = recursiveTypes.ContainsKey(elementType);
            var preloadTextWriter = elementType.IsPrimitiveType() || isRecursive;

            var notNull = emit.DefineLabel();

            if (inLocal != null)
            {
                emit.LoadLocal(inLocal);
            }
            else
            {
                emit.LoadArgument(1);
            }

            var end = emit.DefineLabel();

            emit.BranchIfTrue(notNull);
            WriteString("null", emit);
            emit.Branch(end);

            emit.MarkLabel(notNull);
            WriteString("[", emit);

            var done = emit.DefineLabel();

            using (var e = emit.DeclareLocal(iEnumerableGetEnumerator.ReturnType))
            {
                if (inLocal != null)
                {
                    emit.LoadLocal(inLocal);
                }
                else
                {
                    emit.LoadArgument(1);
                }

                emit.CastClass(iList);                        // IList<>
                emit.CallVirtual(iEnumerableGetEnumerator);   // IEnumerator<>
                emit.StoreLocal(e);                           // --empty--

                // Do the whole first element before the loop starts, so we don't need a branch to emit a ','
                {
                    emit.LoadLocal(e);                      // IEnumerator<>
                    emit.CallVirtual(enumeratorMoveNext);   // bool
                    emit.BranchIfFalse(done);               // --empty--

                    if (isRecursive)
                    {
                        var loc = recursiveTypes[elementType];

                        emit.LoadLocal(loc);                // Action<TextWriter, elementType>
                    }

                    if (preloadTextWriter)
                    {
                        emit.LoadArgument(0);               // Action<>? TextWriter
                    }

                    emit.LoadLocal(e);                      // Action<>? TextWriter? IEnumerator<>
                    emit.CallVirtual(enumeratorCurrent);    // Action<>? TextWriter? type

                    WriteElement(elementType, emit, recursiveTypes);   // --empty--
                }

                var loop = emit.DefineLabel();

                emit.MarkLabel(loop);

                emit.LoadLocal(e);                      // IEnumerator<>
                emit.CallVirtual(enumeratorMoveNext);   // bool
                emit.BranchIfFalse(done);               // --empty--

                if (isRecursive)
                {
                    var loc = recursiveTypes[elementType];

                    emit.LoadLocal(loc);                // Action<TextWriter, elementType>
                }

                if (preloadTextWriter)
                {
                    emit.LoadArgument(0);               // Action<>? TextWriter
                }

                emit.LoadLocal(e);                      // Action<>? TextWriter? IEnumerator<>
                emit.CallVirtual(enumeratorCurrent);    // Action<>? TextWriter? type

                WriteString(",", emit);

                WriteElement(elementType, emit, recursiveTypes);   // --empty--

                emit.Branch(loop);
            }

            emit.MarkLabel(done);

            WriteString("]", emit);

            emit.MarkLabel(end);
        }

        static void WriteElement(Type elementType, Emit emit, Dictionary<Type, Sigil.Local> recursiveTypes)
        {
            if (elementType.IsPrimitiveType())
            {
                WritePrimitive(elementType, emit);
                return;
            }

            var isRecursive = recursiveTypes.ContainsKey(elementType);
            if (isRecursive)
            {
                // Stack is:
                //  - serializingType
                //  - TextWriter
                //  - Action<TextWriter, serializingType>

                var recursiveAct = typeof(Action<,>).MakeGenericType(typeof(TextWriter), elementType);
                var invoke = recursiveAct.GetMethod("Invoke");

                emit.Call(invoke);

                return;
            }

            using(var loc = emit.DeclareLocal(elementType))
            {
                emit.StoreLocal(loc);

                if (elementType.IsListType())
                {
                    WriteList(elementType, emit, recursiveTypes, loc);
                    return;
                }

                if (elementType.IsDictionaryType())
                {
                    WriteDictionary(elementType, emit, recursiveTypes, loc);
                    return;
                }

                WriteObject(elementType, emit, recursiveTypes, loc);
            }
        }

        static void WriteDictionary(Type dictType, Emit emit, Dictionary<Type, Sigil.Local> recursiveTypes, Sigil.Local inLocal = null)
        {
            var elementType = dictType.GetDictionaryInterface().GetGenericArguments()[1];

            var kvType = typeof(KeyValuePair<,>).MakeGenericType(typeof(string), elementType);

            var iEnumerable = typeof(IEnumerable<>).MakeGenericType(kvType);
            var iEnumerableGetEnumerator = iEnumerable.GetMethod("GetEnumerator");
            var enumeratorMoveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext");
            var enumeratorCurrent = iEnumerableGetEnumerator.ReturnType.GetProperty("Current").GetMethod;

            var iDictionary = typeof(IDictionary<,>).MakeGenericType(typeof(string), elementType);

            var isRecursive = recursiveTypes.ContainsKey(elementType);
            var preloadTextWriter = elementType.IsPrimitiveType() || isRecursive;

            var notNull = emit.DefineLabel();

            if (inLocal != null)
            {
                emit.LoadLocal(inLocal);
            }
            else
            {
                emit.LoadArgument(1);
            }

            var end = emit.DefineLabel();

            emit.BranchIfTrue(notNull);
            WriteString("null", emit);
            emit.Branch(end);

            emit.MarkLabel(notNull);
            WriteString("{", emit);

            var done = emit.DefineLabel();

            using (var e = emit.DeclareLocal(iEnumerableGetEnumerator.ReturnType))
            using(var kvpLoc = emit.DeclareLocal(kvType))
            {
                if (inLocal != null)
                {
                    emit.LoadLocal(inLocal);
                }
                else
                {
                    emit.LoadArgument(1);
                }

                emit.CastClass(iDictionary);                  // IDictionary<,>
                emit.CallVirtual(iEnumerableGetEnumerator);   // IEnumerator<KeyValuePair<,>>
                emit.StoreLocal(e);                           // --empty--

                // Do the whole first element before the loop starts, so we don't need a branch to emit a ','
                {
                    emit.LoadLocal(e);                      // IEnumerator<KeyValuePair<,>>
                    emit.CallVirtual(enumeratorMoveNext);   // bool
                    emit.BranchIfFalse(done);               // --empty--

                    if (isRecursive)
                    {
                        var loc = recursiveTypes[elementType];

                        emit.LoadLocal(loc);                // Action<TextWriter, elementType>
                    }

                    if (preloadTextWriter)
                    {
                        emit.LoadArgument(0);               // Action<>? TextWriter
                    }

                    emit.LoadLocal(e);                      // Action<>? TextWriter? IEnumerator<>
                    emit.CallVirtual(enumeratorCurrent);    // Action<>? TextWriter? KeyValuePair<,>

                    emit.StoreLocal(kvpLoc);                // Action<>? TextWriter?
                    emit.LoadLocalAddress(kvpLoc);          // Action<>? TextWriter? KeyValuePair<,>*

                    WriteKeyValue(elementType, emit, recursiveTypes);   // --empty--
                }

                var loop = emit.DefineLabel();

                emit.MarkLabel(loop);

                emit.LoadLocal(e);                      // IEnumerator<KeyValuePair<,>>
                emit.CallVirtual(enumeratorMoveNext);   // bool
                emit.BranchIfFalse(done);               // --empty--

                if (isRecursive)
                {
                    var loc = recursiveTypes[elementType];

                    emit.LoadLocal(loc);                // Action<TextWriter, elementType>
                }

                if (preloadTextWriter)
                {
                    emit.LoadArgument(0);               // Action<>? TextWriter
                }

                emit.LoadLocal(e);                      // Action<>? TextWriter? IEnumerator<>
                emit.CallVirtual(enumeratorCurrent);    // Action<>? TextWriter? KeyValuePair<,>

                emit.StoreLocal(kvpLoc);                // Action<>? TextWriter?
                emit.LoadLocalAddress(kvpLoc);          // Action<>? TextWriter? KeyValuePair<,>*

                WriteString(",", emit);

                WriteKeyValue(elementType, emit, recursiveTypes);   // --empty--
            }

            emit.MarkLabel(done);

            WriteString("}", emit);

            emit.MarkLabel(end);
        }

        static void WriteKeyValue(Type elementType, Emit emit, Dictionary<Type, Sigil.Local> recursiveTypes)
        {
            // top of the stack is a KeyValue<string, elementType>

            var keyValuePair = typeof(KeyValuePair<,>).MakeGenericType(typeof(string), elementType);
            var key = keyValuePair.GetProperty("Key").GetMethod;
            var value = keyValuePair.GetProperty("Value").GetMethod;

            WriteString("\"", emit);

            emit.Duplicate();   // kvp kvp
            emit.Call(key);     // kvp string

            using (var str = emit.DeclareLocal<string>())
            {
                emit.StoreLocal(str);   // kvp
                emit.LoadArgument(0);   // kvp TextWriter
                emit.LoadLocal(str);    // kvp TextWriter string

                emit.CallVirtual(typeof(TextWriter).GetMethod("Write", new [] { typeof(string) })); // kvp
            }

            WriteString("\":", emit);

            emit.Call(value);   // elementType

            if (elementType.IsPrimitiveType())
            {
                WritePrimitive(elementType, emit);
                return;
            }

            var isRecursive = recursiveTypes.ContainsKey(elementType);
            if (isRecursive)
            {
                // Stack is:
                //  - serializingType
                //  - TextWriter
                //  - Action<TextWriter, serializingType>

                var recursiveAct = typeof(Action<,>).MakeGenericType(typeof(TextWriter), elementType);
                var invoke = recursiveAct.GetMethod("Invoke");

                emit.Call(invoke);

                return;
            }

            using (var loc = emit.DeclareLocal(elementType))
            {
                emit.StoreLocal(loc);

                if (elementType.IsListType())
                {
                    WriteList(elementType, emit, recursiveTypes, loc);
                    return;
                }

                if (elementType.IsDictionaryType())
                {
                    WriteList(elementType, emit, recursiveTypes, loc);
                    return;
                }

                WriteObject(elementType, emit, recursiveTypes, loc);
            }
        }

        static Dictionary<Type, Sigil.Local> PreloadRecursiveTypes(HashSet<Type> recursiveTypes, Emit emit)
        {
            var ret = new Dictionary<Type, Sigil.Local>();

            foreach (var type in recursiveTypes)
            {
                var cacheType = typeof(TypeCache<>).MakeGenericType(type);
                var thunk = cacheType.GetField("Thunk", BindingFlags.Public | BindingFlags.Static);

                var loc = emit.DeclareLocal(thunk.FieldType);

                emit.LoadField(thunk);  // Action<TextWriter, type>
                emit.StoreLocal(loc);   // --empty--

                ret[type] = loc;
            }

            return ret;
        }

        static Action<TextWriter, ForType> BuildObjectWithNewDelegate<ForType>()
        {
            var recursiveTypes = FindRecursiveTypes(typeof(ForType));

            var emit = Emit.NewDynamicMethod(typeof(void), new[] { typeof(TextWriter), typeof(ForType) });

            var preloaded = PreloadRecursiveTypes(recursiveTypes, emit);

            WriteObject(typeof(ForType), emit, preloaded);
            emit.Return();

            return emit.CreateDelegate<Action<TextWriter, ForType>>();
        }

        static Action<TextWriter, ListType> BuildListWithNewDelegate<ListType>()
        {
            var recursiveTypes = FindRecursiveTypes(typeof(ListType));

            var emit = Emit.NewDynamicMethod(typeof(void), new[] { typeof(TextWriter), typeof(ListType) });

            var preloaded = PreloadRecursiveTypes(recursiveTypes, emit);

            WriteList(typeof(ListType), emit, preloaded);
            emit.Return();

            return emit.CreateDelegate<Action<TextWriter, ListType>>();
        }

        static Action<TextWriter, DictType> BuildDictionaryWithNewDelegate<DictType>()
        {
            var recursiveTypes = FindRecursiveTypes(typeof(DictType));

            var emit = Emit.NewDynamicMethod(typeof(void), new[] { typeof(TextWriter), typeof(DictType) });

            var preloaded = PreloadRecursiveTypes(recursiveTypes, emit);

            WriteDictionary(typeof(DictType), emit, preloaded);
            emit.Return();

            return emit.CreateDelegate<Action<TextWriter, DictType>>();
        }

        public static Action<TextWriter, ForType> Build<ForType>()
        {
            var forType = typeof(ForType);

            if (forType.IsPrimitiveType())
            {
                return LookupPrimitiveType<ForType>();
            }

            if (forType.IsDictionaryType())
            {
                return BuildDictionaryWithNewDelegate<ForType>();
            }

            if (forType.IsListType())
            {
                return BuildListWithNewDelegate<ForType>();
            }

            if (forType.IsValueType) throw new NotImplementedException();

            return BuildObjectWithNewDelegate<ForType>();
        }
    }
}