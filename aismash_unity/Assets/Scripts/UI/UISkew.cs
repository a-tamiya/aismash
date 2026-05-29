using UnityEngine;
using UnityEngine.UI;

namespace PromptFighters.UI
{
    // uGUIの矩形メッシュを横方向にシアー（傾斜）させ、平行四辺形のアーケード調パーツを作る。
    // Image/TextMeshProUGUI 等の Graphic にアタッチして使用する。
    [RequireComponent(typeof(RectTransform))]
    public class UISkew : BaseMeshEffect
    {
        // 高さ1あたりのX方向ずれ量(px)。正の値で上端が右に寄る。
        public float slantPixels = 14f;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;

            var rt = transform as RectTransform;
            float height = rt.rect.height;
            if (height <= 0.0001f) return;

            // 矩形中央(y=0付近)を基準に、上下で逆方向へずらして平行四辺形化する。
            float pivotY = rt.rect.yMin + height * 0.5f;

            var v = new UIVertex();
            int count = vh.currentVertCount;
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float t = (v.position.y - pivotY) / height; // -0.5..0.5
                v.position.x += t * slantPixels;
                vh.SetUIVertex(v, i);
            }
        }
    }
}
