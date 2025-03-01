﻿#if !NO_RUNTIME
using System;

using ProtoBuf.Serializers;
using System.Globalization;
using System.Collections.Generic;
using System.Reflection;

namespace ProtoBuf.Meta
{
    /// <summary>
    /// Represents a member (property/field) that is mapped to a protobuf field
    /// </summary>
    public class ValueMember
    {
        /// <summary>
        /// The number that identifies this member in a protobuf stream
        /// </summary>
        public int FieldNumber { get; }

        private MemberInfo backingMember;
        /// <summary>
        /// Gets the member (field/property) which this member relates to.
        /// </summary>
        public MemberInfo Member { get; }

        /// <summary>
        /// Gets the backing member (field/property) which this member relates to
        /// </summary>
        public MemberInfo BackingMember
        {
            get { return backingMember; }
            set
            {
                if (backingMember != value)
                {
                    ThrowIfFrozen();
                    backingMember = value;
                }
            }
        }

        private object _defaultValue;

        /// <summary>
        /// Within a list / array / etc, the type of object for each item in the list (especially useful with ArrayList)
        /// </summary>
        public Type ItemType { get; }

        /// <summary>
        /// The underlying type of the member
        /// </summary>
        public Type MemberType { get; }

        /// <summary>
        /// For abstract types (IList etc), the type of concrete object to create (if required)
        /// </summary>
        public Type DefaultType { get; }

        /// <summary>
        /// The type the defines the member
        /// </summary>
        public Type ParentType { get; }

        /// <summary>
        /// The default value of the item (members with this value will not be serialized)
        /// </summary>
        public object DefaultValue
        {
            get { return _defaultValue; }
            set
            {
                if (_defaultValue != value)
                {
                    ThrowIfFrozen();
                    _defaultValue = value;
                }
            }
        }

        private readonly RuntimeTypeModel model;
        /// <summary>
        /// Creates a new ValueMember instance
        /// </summary>
        public ValueMember(RuntimeTypeModel model, Type parentType, int fieldNumber, MemberInfo member, Type memberType, Type itemType, Type defaultType, DataFormat dataFormat, object defaultValue)
            : this(model, fieldNumber, memberType, itemType, defaultType, dataFormat)
        {
            if (parentType == null) throw new ArgumentNullException(nameof(parentType));
            if (fieldNumber < 1 && !Helpers.IsEnum(parentType)) throw new ArgumentOutOfRangeException(nameof(fieldNumber));

            Member = member ?? throw new ArgumentNullException(nameof(member));
            ParentType = parentType;
            if (fieldNumber < 1 && !Helpers.IsEnum(parentType)) throw new ArgumentOutOfRangeException(nameof(fieldNumber));
            
            if (defaultValue != null && (defaultValue.GetType() != memberType))
            {
                defaultValue = ParseDefaultValue(memberType, defaultValue);
            }
            _defaultValue = defaultValue;

            MetaType type = model.FindWithoutAdd(memberType);
            if (type != null)
            {
                AsReference = type.AsReferenceDefault;
            }
            else
            { // we need to scan the hard way; can't risk recursion by fully walking it
                AsReference = MetaType.GetAsReferenceDefault(model, memberType);
            }
        }
        /// <summary>
        /// Creates a new ValueMember instance
        /// </summary>
        internal ValueMember(RuntimeTypeModel model, int fieldNumber, Type memberType, Type itemType, Type defaultType, DataFormat dataFormat)
        {
            FieldNumber = fieldNumber;
            MemberType = memberType ?? throw new ArgumentNullException(nameof(memberType));
            ItemType = itemType;
            DefaultType = defaultType;

            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.dataFormat = dataFormat;
        }
        internal object GetRawEnumValue()
        {
            return ((FieldInfo)Member).GetRawConstantValue();
        }
        private static object ParseDefaultValue(Type type, object value)
        {
            {
                Type tmp = Helpers.GetUnderlyingType(type);
                if (tmp != null) type = tmp;
            }
            if (value is string s)
            {
                if (Helpers.IsEnum(type)) return Helpers.ParseEnum(type, s);

                switch (Helpers.GetTypeCode(type))
                {
                    case ProtoTypeCode.Boolean: return bool.Parse(s);
                    case ProtoTypeCode.Byte: return byte.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    case ProtoTypeCode.Char: // char.Parse missing on CF/phone7
                        if (s.Length == 1) return s[0];
                        throw new FormatException("Single character expected: \"" + s + "\"");
                    case ProtoTypeCode.DateTime: return DateTime.Parse(s, CultureInfo.InvariantCulture);
                    case ProtoTypeCode.Decimal: return decimal.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture);
                    case ProtoTypeCode.Double: return double.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture);
                    case ProtoTypeCode.Int16: return short.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture);
                    case ProtoTypeCode.Int32: return int.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture);
                    case ProtoTypeCode.Int64: return long.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture);
                    case ProtoTypeCode.SByte: return sbyte.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    case ProtoTypeCode.Single: return float.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture);
                    case ProtoTypeCode.String: return s;
                    case ProtoTypeCode.UInt16: return ushort.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture);
                    case ProtoTypeCode.UInt32: return uint.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture);
                    case ProtoTypeCode.UInt64: return ulong.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture);
                    case ProtoTypeCode.TimeSpan: return TimeSpan.Parse(s);
                    case ProtoTypeCode.Uri: return s; // Uri is decorated as string
                    case ProtoTypeCode.Guid: return new Guid(s);
                }
            }

            if (Helpers.IsEnum(type)) return Enum.ToObject(type, value);
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        private IProtoSerializer serializer;
        internal IProtoSerializer Serializer
        {
            get
            {
                return serializer ?? (serializer = BuildSerializer());
            }
        }

        private DataFormat dataFormat;
        /// <summary>
        /// Specifies the rules used to process the field; this is used to determine the most appropriate
        /// wite-type, but also to describe subtypes <i>within</i> that wire-type (such as SignedVariant)
        /// </summary>
        public DataFormat DataFormat
        {
            get { return dataFormat; }
            set
            {
                if (value != dataFormat)
                {
                    ThrowIfFrozen();
                    this.dataFormat = value;
                }
            }
        }

        /// <summary>
        /// Indicates whether this field should follow strict encoding rules; this means (for example) that if a "fixed32"
        /// is encountered when "variant" is defined, then it will fail (throw an exception) when parsing. Note that
        /// when serializing the defined type is always used.
        /// </summary>
        public bool IsStrict
        {
            get { return HasFlag(OPTIONS_IsStrict); }
            set { SetFlag(OPTIONS_IsStrict, value, true); }
        }

        /// <summary>
        /// Indicates whether this field should use packed encoding (which can save lots of space for repeated primitive values).
        /// This option only applies to list/array data of primitive types (int, double, etc).
        /// </summary>
        public bool IsPacked
        {
            get { return HasFlag(OPTIONS_IsPacked); }
            set { SetFlag(OPTIONS_IsPacked, value, true); }
        }

        /// <summary>
        /// Indicates whether this field should *repace* existing values (the default is false, meaning *append*).
        /// This option only applies to list/array data.
        /// </summary>
        public bool OverwriteList
        {
            get { return HasFlag(OPTIONS_OverwriteList); }
            set { SetFlag(OPTIONS_OverwriteList, value, true); }
        }

        /// <summary>
        /// Indicates whether this field is mandatory.
        /// </summary>
        public bool IsRequired
        {
            get { return HasFlag(OPTIONS_IsRequired); }
            set { SetFlag(OPTIONS_IsRequired, value, true); }
        }

        /// <summary>
        /// Enables full object-tracking/full-graph support.
        /// </summary>
        public bool AsReference
        {
            get { return HasFlag(OPTIONS_AsReference); }
            set { SetFlag(OPTIONS_AsReference, value, true); }
        }

        /// <summary>
        /// Embeds the type information into the stream, allowing usage with types not known in advance.
        /// </summary>
        public bool DynamicType
        {
            get { return HasFlag(OPTIONS_DynamicType); }
            set { SetFlag(OPTIONS_DynamicType, value, true); }
        }

        /// <summary>
        /// Indicates that the member should be treated as a protobuf Map
        /// </summary>
        public bool IsMap
        {
            get { return HasFlag(OPTIONS_IsMap); }
            set { SetFlag(OPTIONS_IsMap, value, true); }
        }

        private DataFormat mapKeyFormat, mapValueFormat;
        /// <summary>
        /// Specifies the data-format that should be used for the key, when IsMap is enabled
        /// </summary>
        public DataFormat MapKeyFormat
        {
            get { return mapKeyFormat; }
            set
            {
                if (mapKeyFormat != value)
                {
                    ThrowIfFrozen();
                    mapKeyFormat = value;
                }
            }
        }
        /// <summary>
        /// Specifies the data-format that should be used for the value, when IsMap is enabled
        /// </summary>
        public DataFormat MapValueFormat
        {
            get { return mapValueFormat; }
            set
            {
                if (mapValueFormat != value)
                {
                    ThrowIfFrozen();
                    mapValueFormat = value;
                }
            }
        }

        private MethodInfo getSpecified, setSpecified;
        /// <summary>
        /// Specifies methods for working with optional data members.
        /// </summary>
        /// <param name="getSpecified">Provides a method (null for none) to query whether this member should
        /// be serialized; it must be of the form "bool {Method}()". The member is only serialized if the
        /// method returns true.</param>
        /// <param name="setSpecified">Provides a method (null for none) to indicate that a member was
        /// deserialized; it must be of the form "void {Method}(bool)", and will be called with "true"
        /// when data is found.</param>
        public void SetSpecified(MethodInfo getSpecified, MethodInfo setSpecified)
        {
            if (this.getSpecified != getSpecified || this.setSpecified != setSpecified)
            {
                if (getSpecified != null)
                {
                    if (getSpecified.ReturnType != typeof(bool)
                        || getSpecified.IsStatic
                        || getSpecified.GetParameters().Length != 0)
                    {
                        throw new ArgumentException("Invalid pattern for checking member-specified", nameof(getSpecified));
                    }
                }
                if (setSpecified != null)
                {
                    ParameterInfo[] args;
                    if (setSpecified.ReturnType != typeof(void)
                        || setSpecified.IsStatic
                        || (args = setSpecified.GetParameters()).Length != 1
                        || args[0].ParameterType != typeof(bool))
                    {
                        throw new ArgumentException("Invalid pattern for setting member-specified", nameof(setSpecified));
                    }
                }

                ThrowIfFrozen();
                this.getSpecified = getSpecified;
                this.setSpecified = setSpecified;
            }
        }

        private void ThrowIfFrozen()
        {
            if (serializer != null) throw new InvalidOperationException("The type cannot be changed once a serializer has been generated");
        }

        internal bool ResolveMapTypes(out Type dictionaryType, out Type keyType, out Type valueType)
        {
            dictionaryType = keyType = valueType = null;
            try
            {
                var info = MemberType;
                if (ImmutableCollectionDecorator.IdentifyImmutable(MemberType, out _, out _, out _, out _, out _, out _))
                {
                    return false;
                }
                if (info.IsInterface && info.IsGenericType && info.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                {
                    var typeArgs = MemberType.GetGenericArguments();
                    if (IsValidMapKeyType(typeArgs[0]))
                    {
                        keyType = typeArgs[0];
                        valueType = typeArgs[1];
                        dictionaryType = MemberType;
                    }
                    return false;
                }
                foreach (var iType in MemberType.GetInterfaces())
                {
                    info = iType;

                    if (info.IsGenericType && info.GetGenericTypeDefinition() == typeof(IDictionary<,>))
                    {
                        if (dictionaryType != null) throw new InvalidOperationException("Multiple dictionary interfaces implemented by type: " + MemberType.FullName);
                        var typeArgs = iType.GetGenericArguments();

                        if (IsValidMapKeyType(typeArgs[0]))
                        {
                            keyType = typeArgs[0];
                            valueType = typeArgs[1];
                            dictionaryType = MemberType;
                        }
                    }
                }
                if (dictionaryType == null) return false;

                // (note we checked the key type already)
                // not a map if value is repeated
                Type itemType = null, defaultType = null;
                model.ResolveListTypes(valueType, ref itemType, ref defaultType);
                if (itemType != null) return false;

                return dictionaryType != null;
            }
            catch
            {
                // if it isn't a good fit; don't use "map"
                return false;
            }
        }

        private static bool IsValidMapKeyType(Type type)
        {
            if (type == null || Helpers.IsEnum(type)) return false;
            switch (Helpers.GetTypeCode(type))
            {
                case ProtoTypeCode.Boolean:
                case ProtoTypeCode.Byte:
                case ProtoTypeCode.Char:
                case ProtoTypeCode.Int16:
                case ProtoTypeCode.Int32:
                case ProtoTypeCode.Int64:
                case ProtoTypeCode.String:

                case ProtoTypeCode.SByte:
                case ProtoTypeCode.UInt16:
                case ProtoTypeCode.UInt32:
                case ProtoTypeCode.UInt64:
                    return true;
            }
            return false;
        }
        private IProtoSerializer BuildSerializer()
        {
            int opaqueToken = 0;
            try
            {
                model.TakeLock(ref opaqueToken);// check nobody is still adding this type
                var member = backingMember ?? Member;
                IProtoSerializer ser;
                if (IsMap)
                {
                    ResolveMapTypes(out var dictionaryType, out var keyType, out var valueType);

                    if (dictionaryType == null)
                    {
                        throw new InvalidOperationException("Unable to resolve map type for type: " + MemberType.FullName);
                    }
                    var concreteType = DefaultType;
                    if (concreteType == null && MemberType.IsClass)
                    {
                        concreteType = MemberType;
                    }
                    var keySer = TryGetCoreSerializer(model, MapKeyFormat, keyType, out var keyWireType, false, false, false, false);
                    if (!AsReference)
                    {
                        AsReference = MetaType.GetAsReferenceDefault(model, valueType);
                    }
                    var valueSer = TryGetCoreSerializer(model, MapValueFormat, valueType, out var valueWireType, AsReference, DynamicType, false, true);
                    var ctors = typeof(MapDecorator<,,>).MakeGenericType(new Type[] { dictionaryType, keyType, valueType }).GetConstructors(
                        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    if (ctors.Length != 1) throw new InvalidOperationException("Unable to resolve MapDecorator constructor");
                    ser = (IProtoSerializer)ctors[0].Invoke(new object[] {concreteType, keySer, valueSer, FieldNumber,
                        DataFormat == DataFormat.Group ? WireType.StartGroup : WireType.String, keyWireType, valueWireType, OverwriteList });
                }
                else
                {
                    Type finalType = ItemType ?? MemberType;
                    ser = TryGetCoreSerializer(model, dataFormat, finalType, out WireType wireType, AsReference, DynamicType, OverwriteList, true);
                    if (ser == null)
                    {
                        throw new InvalidOperationException("No serializer defined for type: " + finalType.FullName);
                    }

                    // apply tags
                    if (ItemType != null && SupportNull)
                    {
                        if (IsPacked)
                        {
                            throw new NotSupportedException("Packed encodings cannot support null values");
                        }
                        ser = new TagDecorator(NullDecorator.Tag, wireType, IsStrict, ser);
                        ser = new NullDecorator(ser);
                        ser = new TagDecorator(FieldNumber, WireType.StartGroup, false, ser);
                    }
                    else
                    {
                        ser = new TagDecorator(FieldNumber, wireType, IsStrict, ser);
                    }
                    // apply lists if appropriate
                    if (ItemType != null)
                    {
                        Type underlyingItemType = SupportNull ? ItemType : Helpers.GetUnderlyingType(ItemType) ?? ItemType;

                        Helpers.DebugAssert(underlyingItemType == ser.ExpectedType
                            || (ser.ExpectedType == typeof(object) && !Helpers.IsValueType(underlyingItemType))
                            , "Wrong type in the tail; expected {0}, received {1}", ser.ExpectedType, underlyingItemType);
                        if (MemberType.IsArray)
                        {
                            ser = new ArrayDecorator(ser, FieldNumber, IsPacked, wireType, MemberType, OverwriteList, SupportNull);
                        }
                        else
                        {
                            ser = ListDecorator.Create(MemberType, DefaultType, ser, FieldNumber, IsPacked, wireType, member != null && PropertyDecorator.CanWrite(member), OverwriteList, SupportNull);
                        }
                    }
                    else if (_defaultValue != null && !IsRequired && getSpecified == null)
                    {   // note: "ShouldSerialize*" / "*Specified" / etc ^^^^ take precedence over defaultValue,
                        // as does "IsRequired"
                        ser = new DefaultValueDecorator(_defaultValue, ser);
                    }
                    if (MemberType == typeof(Uri))
                    {
                        ser = new UriDecorator(ser);
                    }
                }
                if (member != null)
                {
                    if (member is PropertyInfo prop)
                    {
                        ser = new PropertyDecorator(ParentType, prop, ser);
                    }
                    else if (member is FieldInfo fld)
                    {
                        ser = new FieldDecorator(ParentType, fld, ser);
                    }
                    else
                    {
                        throw new InvalidOperationException();
                    }

                    if (getSpecified != null || setSpecified != null)
                    {
                        ser = new MemberSpecifiedDecorator(getSpecified, setSpecified, ser);
                    }
                }
                return ser;
            }
            finally
            {
                model.ReleaseLock(opaqueToken);
            }
        }

        private static WireType GetIntWireType(DataFormat format, int width)
        {
            switch (format)
            {
                case DataFormat.ZigZag: return WireType.SignedVariant;
                case DataFormat.FixedSize: return width == 32 ? WireType.Fixed32 : WireType.Fixed64;
                case DataFormat.TwosComplement:
                case DataFormat.Default: return WireType.Variant;
                default: throw new InvalidOperationException();
            }
        }
        private static WireType GetDateTimeWireType(DataFormat format)
        {
            switch (format)
            {
                case DataFormat.Group: return WireType.StartGroup;
                case DataFormat.FixedSize: return WireType.Fixed64;
                case DataFormat.WellKnown:
                case DataFormat.Default:
                    return WireType.String;
                default: throw new InvalidOperationException();
            }
        }

        internal static IProtoSerializer TryGetCoreSerializer(RuntimeTypeModel model, DataFormat dataFormat, Type type, out WireType defaultWireType,
            bool asReference, bool dynamicType, bool overwriteList, bool allowComplexTypes)
        {
            {
                Type tmp = Helpers.GetUnderlyingType(type);
                if (tmp != null) type = tmp;
            }
            if (Helpers.IsEnum(type))
            {
                if (allowComplexTypes && model != null)
                {
                    // need to do this before checking the typecode; an int enum will report Int32 etc
                    defaultWireType = WireType.Variant;
                    return new EnumSerializer(type, model.GetEnumMap(type));
                }
                else
                { // enum is fine for adding as a meta-type
                    defaultWireType = WireType.None;
                    return null;
                }
            }
            ProtoTypeCode code = Helpers.GetTypeCode(type);
            switch (code)
            {
                case ProtoTypeCode.Int32:
                    defaultWireType = GetIntWireType(dataFormat, 32);
                    return PrimitiveSerializer<Int32Serializer>.Singleton;
                case ProtoTypeCode.UInt32:
                    defaultWireType = GetIntWireType(dataFormat, 32);
                    return PrimitiveSerializer<UInt32Serializer>.Singleton;
                case ProtoTypeCode.Int64:
                    defaultWireType = GetIntWireType(dataFormat, 64);
                    return PrimitiveSerializer<Int64Serializer>.Singleton;
                case ProtoTypeCode.UInt64:
                    defaultWireType = GetIntWireType(dataFormat, 64);
                    return PrimitiveSerializer<UInt64Serializer>.Singleton;
                case ProtoTypeCode.String:
                    defaultWireType = WireType.String;
                    if (asReference)
                    {
                        return new NetObjectSerializer(typeof(string), 0, BclHelpers.NetObjectOptions.AsReference);
                    }
                    return PrimitiveSerializer<StringSerializer>.Singleton;
                case ProtoTypeCode.Single:
                    defaultWireType = WireType.Fixed32;
                    return PrimitiveSerializer<SingleSerializer>.Singleton;
                case ProtoTypeCode.Double:
                    defaultWireType = WireType.Fixed64;
                    return PrimitiveSerializer<DoubleSerializer>.Singleton;
                case ProtoTypeCode.Boolean:
                    defaultWireType = WireType.Variant;
                    return PrimitiveSerializer<BooleanSerializer>.Singleton;
                case ProtoTypeCode.DateTime:
                    defaultWireType = GetDateTimeWireType(dataFormat);
                    return new DateTimeSerializer(dataFormat, model);
                case ProtoTypeCode.Decimal:
                    defaultWireType = WireType.String;
                    return PrimitiveSerializer<DecimalSerializer>.Singleton;
                case ProtoTypeCode.Byte:
                    defaultWireType = GetIntWireType(dataFormat, 32);
                    return PrimitiveSerializer<ByteSerializer>.Singleton;
                case ProtoTypeCode.SByte:
                    defaultWireType = GetIntWireType(dataFormat, 32);
                    return PrimitiveSerializer<SByteSerializer>.Singleton;
                case ProtoTypeCode.Char:
                    defaultWireType = WireType.Variant;
                    return PrimitiveSerializer<CharSerializer>.Singleton;
                case ProtoTypeCode.Int16:
                    defaultWireType = GetIntWireType(dataFormat, 32);
                    return PrimitiveSerializer<Int16Serializer>.Singleton;
                case ProtoTypeCode.UInt16:
                    defaultWireType = GetIntWireType(dataFormat, 32);
                    return PrimitiveSerializer<UInt16Serializer>.Singleton;
                case ProtoTypeCode.TimeSpan:
                    defaultWireType = GetDateTimeWireType(dataFormat);
                    return new TimeSpanSerializer(dataFormat);
                case ProtoTypeCode.Guid:
                    defaultWireType = dataFormat == DataFormat.Group ? WireType.StartGroup : WireType.String;
                    return PrimitiveSerializer<GuidSerializer>.Singleton;
                case ProtoTypeCode.Uri:
                    defaultWireType = WireType.String;
                    return PrimitiveSerializer<StringSerializer>.Singleton;
                case ProtoTypeCode.ByteArray:
                    defaultWireType = WireType.String;
                    return new BlobSerializer(overwriteList);
                case ProtoTypeCode.Type:
                    defaultWireType = WireType.String;
                    return PrimitiveSerializer<SystemTypeSerializer>.Singleton;
            }
            IProtoSerializer parseable = model.AllowParseableTypes ? ParseableSerializer.TryCreate(type) : null;
            if (parseable != null)
            {
                defaultWireType = WireType.String;
                return parseable;
            }
            if (allowComplexTypes && model != null)
            {
                int key = model.GetKey(type, false, true);
                MetaType meta = null;
                if (key >= 0)
                {
                    meta = model[type];
                    if (dataFormat == DataFormat.Default && meta.IsGroup)
                    {
                        dataFormat = DataFormat.Group;
                    }
                }

                if (asReference || dynamicType)
                {
                    BclHelpers.NetObjectOptions options = BclHelpers.NetObjectOptions.None;
                    if (asReference) options |= BclHelpers.NetObjectOptions.AsReference;
                    if (dynamicType) options |= BclHelpers.NetObjectOptions.DynamicType;
                    if (meta != null)
                    { // exists
                        if (asReference && Helpers.IsValueType(type))
                        {
                            string message = "AsReference cannot be used with value-types";

                            if (type.Name == "KeyValuePair`2")
                            {
                                message += "; please see https://stackoverflow.com/q/14436606/23354";
                            }
                            else
                            {
                                message += ": " + type.FullName;
                            }
                            throw new InvalidOperationException(message);
                        }

                        if (asReference && (meta.IsAutoTuple || meta.HasSurrogate)) options |= BclHelpers.NetObjectOptions.LateSet;
                        if (meta.UseConstructor) options |= BclHelpers.NetObjectOptions.UseConstructor;
                    }
                    defaultWireType = dataFormat == DataFormat.Group ? WireType.StartGroup : WireType.String;
                    return new NetObjectSerializer(type, key, options);
                }
                if (key >= 0)
                {
                    defaultWireType = dataFormat == DataFormat.Group ? WireType.StartGroup : WireType.String;
                    return new SubItemSerializer(type, key, meta, true);
                }
            }
            defaultWireType = WireType.None;
            return null;
        }

        private string name;
        internal void SetName(string name)
        {
            if (name != this.name)
            {
                ThrowIfFrozen();
                this.name = name;
            }
        }
        /// <summary>
        /// Gets the logical name for this member in the schema (this is not critical for binary serialization, but may be used
        /// when inferring a schema).
        /// </summary>
        public string Name
        {
            get { return string.IsNullOrEmpty(name) ? Member.Name : name; }
            set { SetName(value); }
        }

        private const byte
           OPTIONS_IsStrict = 1,
           OPTIONS_IsPacked = 2,
           OPTIONS_IsRequired = 4,
           OPTIONS_OverwriteList = 8,
           OPTIONS_SupportNull = 16,
           OPTIONS_AsReference = 32,
           OPTIONS_IsMap = 64,
           OPTIONS_DynamicType = 128;

        private byte flags;
        private bool HasFlag(byte flag) { return (flags & flag) == flag; }
        private void SetFlag(byte flag, bool value, bool throwIfFrozen)
        {
            if (throwIfFrozen && HasFlag(flag) != value)
            {
                ThrowIfFrozen();
            }
            if (value)
                flags |= flag;
            else
                flags = (byte)(flags & ~flag);
        }

        /// <summary>
        /// Should lists have extended support for null values? Note this makes the serialization less efficient.
        /// </summary>
        public bool SupportNull
        {
            get { return HasFlag(OPTIONS_SupportNull); }
            set { SetFlag(OPTIONS_SupportNull, value, true); }
        }

        internal string GetSchemaTypeName(bool applyNetObjectProxy, ref RuntimeTypeModel.CommonImports imports)
        {
            Type effectiveType = ItemType ?? MemberType;
            return model.GetSchemaTypeName(effectiveType, DataFormat, applyNetObjectProxy && AsReference, applyNetObjectProxy && DynamicType, ref imports);
        }

        internal sealed class Comparer : System.Collections.IComparer, IComparer<ValueMember>
        {
            public static readonly Comparer Default = new Comparer();

            public int Compare(object x, object y)
            {
                return Compare(x as ValueMember, y as ValueMember);
            }

            public int Compare(ValueMember x, ValueMember y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                return x.FieldNumber.CompareTo(y.FieldNumber);
            }
        }
    }
}
#endif