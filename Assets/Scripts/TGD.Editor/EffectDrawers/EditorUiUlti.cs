using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TGD.Editor
{
    internal static class EditorUIUtil
    {
        // 折叠缓存（按 key 记忆）
        private static readonly Dictionary<string, bool> s_Foldouts = new();

        // 颜色与样式
        private static Color PanelBg => EditorGUIUtility.isProSkin
            ? new Color(1, 1, 1, 0.045f)
            : new Color(0, 0, 0, 0.045f);

        private static GUIStyle _headerStyle;
        private static GUIStyle HeaderStyle
        {
            get
            {
                if (_headerStyle == null)
                {
                    _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 12,
                        alignment = TextAnchor.MiddleLeft
                    };
                }
                return _headerStyle;
            }
        }

        private static GUIStyle _tagStyle;
        private static GUIStyle TagStyle
        {
            get
            {
                if (_tagStyle == null)
                {
                    _tagStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        padding = new RectOffset(4, 4, 1, 1)
                    };
                }
                return _tagStyle;
            }
        }

        private static GUIStyle _subtle;
        public static GUIStyle Subtle
        {
            get
            {
                if (_subtle == null)
                {
                    _subtle = new GUIStyle(EditorStyles.label)
                    {
                        normal = { textColor = EditorStyles.miniLabel.normal.textColor },
                        fontSize = 10
                    };
                }
                return _subtle;
            }
        }

        public static void Separator(float height = 1f, float vSpace = 4f)
        {
            EditorGUILayout.Space(vSpace);
            var r = GUILayoutUtility.GetRect(1, height, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(r, new Color(0, 0, 0, EditorGUIUtility.isProSkin ? 0.35f : 0.12f));
            EditorGUILayout.Space(vSpace);
        }

        public static void BoxScope(System.Action body, float headerPad = 6f)
        {
            var r = EditorGUILayout.BeginVertical("box");
            if (Event.current.type == EventType.Repaint)
            {
                var bg = r;
                bg.height += headerPad;
                EditorGUI.DrawRect(bg, PanelBg);
            }
            body?.Invoke();
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 左标题 + 右侧小徽标
        /// </summary>
        public static void Header(string title, string rightTag = null, Color? tagColor = null)
        {
            var rect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new Color(0, 0, 0, EditorGUIUtility.isProSkin ? 0.06f : 0.045f));

            // 左标题
            var left = rect;
            left.xMin += 6f;
            GUI.Label(left, title, HeaderStyle);

            // 右徽标
            if (!string.IsNullOrEmpty(rightTag))
            {
                var content = new GUIContent(rightTag);
                var size = TagStyle.CalcSize(content);
                var pad = 6f;
                var badge = new Rect(rect.xMax - size.x - pad - 4f, rect.y + 3f, size.x + pad, rect.height - 6f);
                var c = tagColor ?? (EditorGUIUtility.isProSkin ? new Color(0.2f, 0.6f, 1f, 0.20f) : new Color(0.2f, 0.5f, 1f, 0.12f));
                if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(badge, c);
                GUI.Label(badge, content, TagStyle);
            }
        }

        public static bool Foldout(string key, string label, bool defaultState = true)
        {
            if (!s_Foldouts.TryGetValue(key, out var open))
            {
                open = defaultState;
                s_Foldouts[key] = open;
            }

            var rect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            // 背景条
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, new Color(0, 0, 0, EditorGUIUtility.isProSkin ? 0.06f : 0.045f));

            // 折叠按钮
            var foldRect = rect;
            foldRect.xMin += 4f;
            open = EditorGUI.Foldout(foldRect, open, label, true);
            s_Foldouts[key] = open;
            return open;
        }

        public static Color ColorForEffectType(TGD.Data.EffectType type)
        {
            switch (type)
            {
                case TGD.Data.EffectType.Damage: return new Color(1f, 0.3f, 0.3f, 0.22f);
                case TGD.Data.EffectType.Heal: return new Color(0.3f, 1f, 0.5f, 0.22f);
                case TGD.Data.EffectType.Aura: return new Color(0.3f, 0.7f, 1f, 0.22f);
                case TGD.Data.EffectType.RandomOutcome: return new Color(1f, 0.8f, 0.3f, 0.22f);
                case TGD.Data.EffectType.Repeat: return new Color(0.7f, 0.5f, 1f, 0.22f);
                case TGD.Data.EffectType.ModifyStatus: return new Color(1f, 0.5f, 0.8f, 0.22f);
                default: return new Color(0, 0, 0, 0.18f);
            }
        }
    }
}
