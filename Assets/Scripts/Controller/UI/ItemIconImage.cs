using UnityEngine;
using UnityEngine.UI;

public class ItemIconImage : Image
{
    private float sliceIndex = -1f;

    public void SetSliceIndex(float index)
    {
        if (sliceIndex == index) return;
        sliceIndex = index;
        SetVerticesDirty(); // 꼭짓점 정보가 바뀌었으니 다시 그리라고 알림
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        base.OnPopulateMesh(vh);

        // UGUI가 생성한 기본 사각형의 모든 꼭짓점에 슬롯 인덱스 정보를 주입합니다.
        UIVertex vertex = new UIVertex();
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);
            vertex.uv1 = new Vector2(sliceIndex, 0); // uv1(TEXCOORD1)의 x값에 인덱스 저장
            vh.SetUIVertex(vertex, i);
        }
    }
}
