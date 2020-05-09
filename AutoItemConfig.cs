﻿using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Runtime.CompilerServices;
using System.Reflection;
using RoR2;
using System.Text.RegularExpressions;

namespace TILER2 {

    public class AutoItemConfigContainer {
        /// <summary>All config entries generated by AutoItemCfg.Bind will be stored here. Use nameof(targetProperty) to access, if possible (note that this will not protect against type changes while casting to generic ConfigEntry).</summary>
        protected readonly Dictionary<string, ConfigEntryBase> autoItemConfigs = new Dictionary<string, ConfigEntryBase>();
        /// <summary>All config entries generated by AutoItemCfg.Bind(..., BindSubDictInfo) will be stored here. Use nameof(targetProperty) to access, if possible (note that this will not protect against type changes while casting to generic Dictionary/ConfigEntry).</summary>
        protected readonly Dictionary<string, Dictionary<object,ConfigEntryBase>> autoItemConfigDicts = new Dictionary<string, Dictionary<object,ConfigEntryBase>>();

        /// <summary>Fired when any of the config entries tracked by this AutoItemConfigContainer change.</summary>
        public event EventHandler<AutoUpdateEventArgs> ConfigEntryChanged;
        /// <summary>Internal handler for ConfigEntryChanged event.</summary>
        private void OnConfigEntryChanged(AutoUpdateEventArgs e) {
            ConfigEntryChanged?.Invoke(this, e);
            if((e.flags & AutoUpdateEventFlags.InvalidateStats) == AutoUpdateEventFlags.InvalidateStats && (Run.instance?.isActiveAndEnabled ?? false)) {
                Debug.Log("Invalidating stats on " + MiscUtil.AliveList().Count + " CharacterMasters");
                MiscUtil.AliveList().ForEach(cm => {if(cm.hasBody) cm.GetBody().RecalculateStats();});
            }
        }

        private static readonly Dictionary<ConfigFile, DateTime> observedFiles = new Dictionary<ConfigFile, DateTime>();
        private const float filePollingRate = 10f;
        private static float filePollingStopwatch = 0f;

        internal static void FilePollUpdateHook(On.RoR2.RoR2Application.orig_Update orig, RoR2Application self) {
            orig(self);
            filePollingStopwatch += Time.unscaledDeltaTime;
            if(filePollingStopwatch >= filePollingRate) {
                filePollingStopwatch = 0;
                foreach(ConfigFile cfl in observedFiles.Keys.ToList()) {
                    var thisup = System.IO.File.GetLastWriteTime(cfl.ConfigFilePath);
                    if(observedFiles[cfl] < thisup) {
                        observedFiles[cfl] = thisup;
                        Debug.Log("Updating " + cfl.Count + " entries in " + cfl.ConfigFilePath);
                        cfl.Reload();
                    }
                }
            }
        }

        /// <summary>Stores information about an item of a reflected dictionary during iteration.</summary>
        public struct BindSubDictInfo {
            /// <summary>The key of the current element.</summary>
            public object key;
            /// <summary>The value of the current element.</summary>
            public object val;
            /// <summary>The key type of the entire dictionary.</summary>
            public Type keyType;
            /// <summary>The current index of iteration.</summary>
            public int index;
        }
        
        /// <summary>Simple tag replacer for patterns matching &lt;AIC.Param1.Param2...&gt;, using reflection information as the replacing values.<para />
        /// Supported tags: AIC.Prop.[PropName], AIC.DictKey, AIC.DictInd, AIC.DictKeyProp.[PropName]</summary>
        private string ReplaceTags(string orig, PropertyInfo prop, string categoryName, BindSubDictInfo? subDict = null) {
            return Regex.Replace(orig, @"<AIC.([a-zA-Z\.]+)>", (m)=>{
                string[] strParams = Regex.Split(m.Groups[0].Value.Substring(1, m.Groups[0].Value.Length - 2), @"(?<!\\)\.");;
                if(strParams.Length < 2) return m.Value;
                switch(strParams[1]) {
                    case "Prop":
                        if(strParams.Length < 3){
                            Debug.LogWarning("TILER2: AutoItemCfg.Bind on property " + prop.Name + " in category " + categoryName + ": malformed string param \"" + m.Value + "\" (not enough params for Prop tag).");
                            return m.Value;
                        }
                        var iprop = prop.DeclaringType.GetProperty(strParams[2], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if(iprop == null) {
                            Debug.LogWarning("TILER2: AutoItemCfg.Bind on property " + prop.Name + " in category " + categoryName + ": malformed string param \"" + m.Value + "\" (could not find Prop \"" + strParams[2] + "\").");
                            return m.Value;
                        }
                        return iprop.GetValue(this).ToString();
                    case "DictKey":
                        if(!subDict.HasValue) {
                            Debug.LogWarning("TILER2: AutoItemCfg.Bind on property " + prop.Name + " in category " + categoryName + ": malformed string param \"" + m.Value + "\" (DictKey tag used on non-BindDict).");
                            return m.Value;
                        }
                        return subDict.Value.key.ToString();
                    case "DictInd":
                        if(!subDict.HasValue) {
                            Debug.LogWarning("TILER2: AutoItemCfg.Bind on property " + prop.Name + " in category " + categoryName + ": malformed string param \"" + m.Value + "\" (DictInd tag used on non-BindDict).");
                            return m.Value;
                        }
                        return subDict.Value.index.ToString();
                    case "DictKeyProp":
                        if(!subDict.HasValue) {
                            Debug.LogWarning("TILER2: AutoItemCfg.Bind on property " + prop.Name + " in category " + categoryName + ": malformed string param \"" + m.Value + "\" (DictKeyProp tag used on non-BindDict).");
                            return m.Value;
                        }
                        if(strParams.Length < 3){
                            Debug.LogWarning("TILER2: AutoItemCfg.Bind on property " + prop.Name + " in category " + categoryName + ": malformed string param \"" + m.Value + "\" (not enough params for Prop tag).");
                            return m.Value;
                        }
                        PropertyInfo kprop = subDict.Value.key.GetType().GetProperty(strParams[2], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if(kprop == null) {
                            Debug.LogWarning("TILER2: AutoItemCfg.Bind on property " + prop.Name + " in category " + categoryName + ": malformed string param \"" + m.Value + "\" (could not find DictKeyProp \"" + strParams[2] + "\").");
                            return m.Value;
                        }
                        return kprop.GetValue(subDict.Value.key).ToString();
                }
                Debug.LogWarning("TILER2: AutoItemCfg.Bind on property " + prop.Name + " in category " + categoryName + ": malformed string param \"" + m.Value + "\" (unknown tag \"" + strParams[1] + "\").");
                return m.Value;
            });
        }

        public void Bind(PropertyInfo prop, ConfigFile cfl, string categoryName, AutoItemConfigAttribute attrib, AutoUpdateEventInfoAttribute eiattr = null, BindSubDictInfo? subDict = null) {
            if(this.autoItemConfigs.ContainsKey(prop.Name)) {
                Debug.LogError("TILER2: AutoItemCfg.Bind on property " + prop.Name + " in category " + categoryName + ": this property has already been bound.");
                return;
            }
            if((attrib.flags & AutoItemConfigFlags.BindDict) == AutoItemConfigFlags.BindDict && !subDict.HasValue) {
                if(!prop.PropertyType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>))) {
                    Debug.LogError("TILER2: AutoItemCfg.Bind on BindDict property " + prop.Name + " in category " + categoryName + ": BindDict flag cannot be used on property types which don't implement IDictionary.");
                    return;
                }
                var kTyp = prop.PropertyType.GetGenericArguments()[1];
                if(attrib.avb != null && attrib.avbType != kTyp) {
                    Debug.LogError("TILER2: AutoItemCfg.Bind on BindDict property " + prop.Name + " in category " + categoryName + ": dict value and AcceptableValue types must match (received " + kTyp.Name + " and " + attrib.avbType.Name + ").");
                    return;

                }
                autoItemConfigDicts.Add(prop.Name, new Dictionary<object, ConfigEntryBase>());
                var idict = (System.Collections.IDictionary)prop.GetValue(this, null);
                int ind = 0;
                Debug.Log("Dict bind: " + idict);
                Debug.Log(idict.Count);
                var dkeys = (from object k in idict.Keys
                             select k).ToList();
                foreach(object o in dkeys) {
                    Debug.Log(o.ToString() + " > " + idict[o].ToString());
                    Bind(prop, cfl, categoryName, attrib, eiattr, new BindSubDictInfo{key=o, val=idict[o], keyType=kTyp, index=ind});
                    ind++;
                }
                return;
            }
            if(!subDict.HasValue && attrib.avb != null && attrib.avbType != prop.PropertyType) {
                Debug.LogError("TILER2: AutoItemCfg.Bind on property " + prop.Name + " in category " + categoryName + ": property and AcceptableValue types must match (received " + prop.PropertyType.Name + " and " + attrib.avbType.Name + ").");
                return;
            }
            
            object propObj = subDict.HasValue ? prop.GetValue(this) : this;
            var dict = subDict.HasValue ? (System.Collections.IDictionary)propObj : null;
            var propGetter = subDict.HasValue ? dict.GetType().GetProperty("Item").GetGetMethod(true)
                : (prop.GetGetMethod(true) ?? prop.DeclaringType.GetProperty(prop.Name)?.GetGetMethod(true));
            var propSetter = subDict.HasValue ? dict.GetType().GetProperty("Item").GetSetMethod(true)
                : (prop.GetSetMethod(true) ?? prop.DeclaringType.GetProperty(prop.Name)?.GetSetMethod(true));
            var propType = subDict.HasValue ? subDict.Value.keyType : prop.PropertyType;

            if(propGetter == null || propSetter == null) {
                Debug.LogError("TILER2: AutoItemCfg.Bind on property " + prop.Name + " in category " + categoryName + ": property (or dictionary Item property, if using BindDict flag) must have both a getter and a setter.");
                return;
            }

            string cfgName = attrib.name;
            if(cfgName != null) {
                cfgName = ReplaceTags(cfgName, prop, categoryName, subDict);
            } else cfgName = char.ToUpperInvariant(prop.Name[0]) + prop.Name.Substring(1) + (subDict.HasValue ? ":" + subDict.Value.index : "");

            string cfgDesc = attrib.desc;
            if(cfgDesc != null) {
                cfgDesc = ReplaceTags(cfgDesc, prop, categoryName, subDict);
            } else cfgDesc = "Automatically generated from a C# " + (subDict.HasValue ? "dictionary " : "") + "property.";
            
            //Matches ConfigFile.Bind<T>(ConfigDefinition configDefinition, T defaultValue, ConfigDescription configDescription)
            var genm = typeof(ConfigFile).GetMethods().First(
                    x=>x.Name == nameof(ConfigFile.Bind)
                    && x.GetParameters().Length == 3
                    && x.GetParameters()[0].ParameterType == typeof(ConfigDefinition)
                    && x.GetParameters()[2].ParameterType == typeof(ConfigDescription)
                ).MakeGenericMethod(propType);

            var cfe = (ConfigEntryBase)genm.Invoke(cfl, new[] {
                new ConfigDefinition(categoryName, cfgName),
                subDict.HasValue ? subDict.Value.val : prop.GetValue(this),
                new ConfigDescription(cfgDesc,attrib.avb)});

            observedFiles[cfl] = System.IO.File.GetLastWriteTime(cfl.ConfigFilePath);

            if(subDict.HasValue)
                this.autoItemConfigDicts[prop.Name].Add(subDict.Value.key, cfe);
            else
                this.autoItemConfigs.Add(prop.Name, cfe);
            
            bool doCache = false;
            if((attrib.flags & AutoItemConfigFlags.DeferUntilNextStage) == AutoItemConfigFlags.DeferUntilNextStage) {
                doCache = true;
                On.RoR2.Run.OnDisable += (orig, self) => {
                    orig(self);
                    Debug.Log("Run OnDisable fired, updating cval " + categoryName + "." + prop.Name);
                    if(propGetter.Invoke(propObj, subDict.HasValue ? new[]{subDict.Value.key} : new object[]{}) != cfe.BoxedValue) {
                        UpdateProperty(prop, propObj, cfe.BoxedValue, propGetter, propSetter, eiattr, subDict.HasValue ? subDict.Value.key : null);
                    }
                };
            }
            if((attrib.flags & AutoItemConfigFlags.DeferUntilEndGame) == AutoItemConfigFlags.DeferUntilEndGame) {
                doCache = true;
                On.RoR2.Run.EndStage += (orig, self) => {
                    orig(self);
                    Debug.Log("Run EndStage fired, updating cval " + categoryName + "." + prop.Name);
                    if(propGetter.Invoke(propObj, subDict.HasValue ? new[]{subDict.Value.key} : new object[]{}) != cfe.BoxedValue) {
                        UpdateProperty(prop, propObj, cfe.BoxedValue, propGetter, propSetter, eiattr, subDict.HasValue ? subDict.Value.key : null);
                    }
                };
            }

            if((attrib.flags & AutoItemConfigFlags.AllowNetMismatch) == AutoItemConfigFlags.AllowNetMismatch) { //!=
                throw new NotImplementedException("AutoItemConfigFlags.AllowNetMismatch");
            }

            if((attrib.flags & AutoItemConfigFlags.DeferForever) != AutoItemConfigFlags.DeferForever) {
                var gtyp = typeof(ConfigEntry<>).MakeGenericType(propType);
                var evh = gtyp.GetEvent("SettingChanged");
                
                evh.ReflAddEventHandler(cfe, (object obj,EventArgs evtArgs) => {
                    Debug.Log("AutoItemCfg: SettingChanged event fired for " + categoryName + "." + cfgName + " of type " + propType + (subDict.HasValue ? " (in dictionary)" : ""));
                    Debug.Log("RunInstanceEnabled: " + (Run.instance?.enabled.ToString() ?? "no instance"));
                    if(subDict.HasValue) Debug.Log(subDict.Value.key);
                    if(!doCache || Run.instance == null || !Run.instance.enabled) {
                        UpdateProperty(prop, propObj, cfe.BoxedValue, propGetter, propSetter, eiattr, subDict.HasValue ? subDict.Value.key : null);
                    } else {
                        Debug.Log("Deferring update; would be from " + propGetter.Invoke(propObj, subDict.HasValue ? new[] { subDict.Value.key } : new object[]{ }) + " to " + cfe.BoxedValue);
                        //TODO: replace/simplify RoR2.Run event hooks by marking as dirty somehow?
                    }
                });
            }

            if((attrib.flags & AutoItemConfigFlags.ExposeAsConVar) == AutoItemConfigFlags.ExposeAsConVar) {
                throw new NotImplementedException("AutoItemConfigFlags.ExposeAsConVar");
            }

            if((attrib.flags & AutoItemConfigFlags.NoInitialRead) != AutoItemConfigFlags.NoInitialRead)
                propSetter.Invoke(propObj, subDict.HasValue ? new[]{subDict.Value.key, cfe.BoxedValue} : new[]{cfe.BoxedValue});
        }

        public void BindAll(ConfigFile cfl, string categoryName) {
            Debug.Log("BindAll on " + this.GetType().Name);
            foreach(var prop in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                var attrib = prop.GetCustomAttribute<AutoItemConfigAttribute>(true);
                if(attrib != null)
                    this.Bind(prop, cfl, categoryName, attrib, prop.GetCustomAttribute<AutoUpdateEventInfoAttribute>(true));
            }
        }

        private void UpdateProperty(PropertyInfo targetProp, object target, object newValue, MethodInfo getter, MethodInfo setter, AutoUpdateEventInfoAttribute eiattr, object dictKey = null) {
            var oldValue = getter.Invoke(target, (dictKey != null) ? new[] {dictKey} : new object[]{ });
            setter.Invoke(target, (dictKey != null) ? new[]{dictKey, newValue} : new[]{newValue});
            OnConfigEntryChanged(new AutoUpdateEventArgs{
                flags = eiattr?.flags ?? AutoUpdateEventFlags.None,
                oldValue = oldValue,
                newValue = newValue,
                changedProperty = targetProp,
                changedKey = dictKey});
        }
    }


    [Flags]
    public enum AutoItemConfigFlags {
        None = 0,
        ///<summary>If UNSET (default): expects acceptableValues to contain 0 or 2 values, which will be added to an AcceptableValueRange. If SET: an AcceptableValueList will be used instead.</summary>
        AVIsList = 1,
        ///<summary>(TODO: needs testing) If SET: will cache config changes, through auto-update or otherwise, and prevent them from applying to the attached property until the next stage transition.</summary>
        DeferUntilNextStage = 2,
        ///<summary>(TODO: needs testing) If SET: will cache config changes, through auto-update or otherwise, and prevent them from applying to the attached property while there is an active run.</summary>
        DeferUntilEndGame = 4,
        ///<summary>(TODO: needs testing) If SET: the attached property will never be changed by config.</summary>
        DeferForever = 8,
        ///<summary>(TODO: NYI) If SET: will add a ConVar linked to the attached property and config entry.</summary>
        ExposeAsConVar = 16,
        ///<summary>If SET: will stop the property value from being changed by the initial config read during BindAll.</summary>
        NoInitialRead = 32,
        ///<summary>(TODO: net mismatch checking is NYI) If UNSET: the property will temporarily retrieve its value from the host in multiplayer.</summary>
        AllowNetMismatch = 64,
        ///<summary>If SET: will bind individual items in an IDictionary instead of the entire collection.</summary>
        BindDict = 128
    }

    [Flags]
    public enum AutoUpdateEventFlags {
        None = 0,
        ///<summary>Causes an update to the linked item's language registry.</summary>
        InvalidateNameToken = 1,
        ///<summary>Causes an update to the linked item's language registry.</summary>
        InvalidatePickupToken = 2,
        ///<summary>Causes an update to the linked item's language registry.</summary>
        InvalidateDescToken = 4,
        ///<summary>Causes an update to the linked item's language registry.</summary>
        InvalidateLoreToken = 8,
        ///<summary>Causes an update to the linked item's pickup model.</summary>
        InvalidateModel = 16,
        ///<summary>Causes RecalculateStats on all copies of CharacterMaster which are alive and have a CharacterBody.</summary>
        InvalidateStats = 32
    }

    public class AutoUpdateEventArgs : EventArgs {
        public AutoUpdateEventFlags flags;
        public object oldValue;
        public object newValue;
        public object changedKey;
        public PropertyInfo changedProperty;
    }
    
    ///<summary>Causes some actions to be automatically performed when a property's config entry is updated.</summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class AutoUpdateEventInfoAttribute : Attribute {
        public AutoUpdateEventFlags flags {get; private set;}
        public AutoUpdateEventInfoAttribute(AutoUpdateEventFlags flags) {
            this.flags = flags;
        }
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class AutoItemConfigAttribute : Attribute {

        public string name {get; private set;}
        public string desc {get; private set;} = "";
        public AcceptableValueBase avb {get; private set;} = null;
        public Type avbType {get; private set;} = null;
        public AutoItemConfigFlags flags {get; private set;}
        public AutoItemConfigAttribute(string name, string desc, AutoItemConfigFlags flags = AutoItemConfigFlags.None, params object[] acceptableValues) : this(desc, flags, acceptableValues) {
            this.name = name;
        }

        public AutoItemConfigAttribute(string desc, AutoItemConfigFlags flags = AutoItemConfigFlags.None, params object[] acceptableValues) {
            if(acceptableValues.Length > 0) {
                var avList = (flags & AutoItemConfigFlags.AVIsList) == AutoItemConfigFlags.AVIsList;
                if(!avList && acceptableValues.Length != 2) throw new ArgumentException("Range mode for acceptableValues (flag AVIsList not set) requires either 0 or 2 params; received " + acceptableValues.Length + ".\nThe description provided was: \"" + desc + "\".");
                var iType = acceptableValues[0].GetType();
                for(var i = 1; i < acceptableValues.Length; i++) {
                    if(iType != acceptableValues[i].GetType()) throw new ArgumentException("Types of all acceptableValues must match");
                }
                var avbVariety = avList ? typeof(AcceptableValueList<>).MakeGenericType(iType) : typeof(AcceptableValueRange<>).MakeGenericType(iType);
                this.avb = (AcceptableValueBase)Activator.CreateInstance(avbVariety, acceptableValues);
                this.avbType = iType;
            }
            this.desc = desc;
            this.flags = flags;
        }
    }
}
