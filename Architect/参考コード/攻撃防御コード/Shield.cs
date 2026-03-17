using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
[CreateAssetMenu(fileName = "Shield", menuName = "CreateShield")]
public class Shield : Equip
{

 


    [Header("ゲーム画面で表示するスプライト")]
    public Sprite[] back = new Sprite[2];


    public bool weaponArts;//盾ではなく武器の特殊技を使うかどうか


    [Header("盾の固有技のデータ")]
    public MotionChargeImfo artsValue;

    [Header("盾技のアニメーション")]
    ///<summary>
    ///盾固有技のアニメ
    /// </summary>
    public AnimationClip[] artsAnime;

    [Header("チャージした盾固有技のアニメーション")]
    ///<summary>
    ///チャージした盾固有技のアニメ
    /// </summary>
    public AnimationClip[] chargeArtsAnime;


}
