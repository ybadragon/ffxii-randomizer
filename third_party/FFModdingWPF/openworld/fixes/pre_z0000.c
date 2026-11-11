// File size: 4736 Bytes
// Author:    iwabuchi
// Source:    pre_z0000.src
// Date:      2025-03-06 03:43:33.7945297 UTC
// Binary:    prez_edited.bin

option compilerVersion = {3, 0, 0};
option authorName = "iwabuchi";
option fileName = "pre_z0000.src";
option spawnOrder = {-1, -1, -1, -1, -1, -1, -1, -1};
option weatherProbability = {100, 0, 0};
option externalLabels = {
	0x0 //trap offset
};

//======================================================================
//              Savemem and scratchpad variable imports               //
//======================================================================
import savemem  short   シナリオフラグ = 0x00;
import savemem  short   global_flag[256] = 0x00;
import savemem  u_char  g_iw_ラバナスタ通行止め状態 = 0x040a;
import savemem  u_char  g_com_会話段階変数 = 0x0a00;
import savemem  u_char  g_com_パーティ人数変数 = 0x0a01;
import savemem  u_char  g_com_e3_flag = 0x0a02;
import savemem  u_char  g_btl_ミミッククイーン = 0x0a04;
import savemem  u_char  g_btl_プリン = 0x0a05;
import savemem  u_char  g_btl_ブッシュファイア = 0x0a06;
import savemem  u_char  g_btl_ハイドラ = 0x0a07;
import savemem  u_char  g_btl_ティアマット = 0x0a08;
import savemem  u_char  g_btl_ダイダロス = 0x0a09;
import savemem  u_char  g_btl_タイラント = 0x0a0a;
import savemem  u_char  g_btl_メタルドラゴン = 0x0a0b;
import savemem  u_char  g_btl_アーリマン = 0x0a0c;
import savemem  u_char  g_btl_ラフレシア = 0x0a0d;
import savemem  u_char  g_btl_マンドラゴラーズ = 0x0a0e;
import savemem  u_char  g_btl_フンババ = 0x0a0f;
import savemem  u_char  g_btl_フューリー = 0x0a10;
import savemem  u_char  g_btl_エルダードラゴン = 0x0a11;
import savemem  u_char  g_btl_デモンズウォールA = 0x0a14;
import savemem  u_char  g_btl_デモンズウォールB = 0x0a15;
import savemem  u_char  g_btl_ボムキング = 0x0a16;
import savemem  u_char  g_btl_魔神竜 = 0x0a18;
import savemem  u_char  g_btl_ベリアス = 0x0a19;
import savemem  u_char  g_btl_カオス = 0x0a1a;
import savemem  u_char  g_btl_ゾディアーク = 0x0a1b;
import savemem  u_char  g_btl_キュクレイン = 0x0a1c;
import savemem  u_char  g_btl_ザルエラ = 0x0a1d;
import savemem  u_char  g_btl_ハシュマリム = 0x0a1f;
import savemem  u_char  g_btl_シュミハザ = 0x0a20;
import savemem  u_char  g_btl_マティウス = 0x0a21;
import savemem  u_char  g_btl_ゼロムス = 0x0a22;
import savemem  u_char  g_btl_エクスデス = 0x0a23;
import savemem  u_char  g_btl_アルテマ = 0x0a24;
import savemem  u_char  g_btl_アドラメレク = 0x0a25;
import savemem  u_char  g_btl_バッガモナン = 0x0a26;
import savemem  u_char  g_btl_ザコジャッジ = 0x0a27;
import savemem  u_char  g_btl_ギース = 0x0a28;
import savemem  u_char  g_btl_シドドラクロア = 0x0a29;
import savemem  u_char  g_btl_ヴェイン = 0x0a2c;
import savemem  u_char  g_btl_ラスト・ヴェイン = 0x0a2e;
import savemem  u_char  g_btl_ガブラスバハムート = 0x0a30;
import savemem  u_char  g_btl_クリスタルミミックグリーン = 0x0a31;
import savemem  u_char  g_btl_クリスタルミミックブルー = 0x0a32;
import savemem  u_char  g_btl_クリスタルミミックレッド = 0x0a33;
import savemem  u_char  g_btl_ゲンブ = 0x0a34;
import savemem  u_char  g_btl_セイリュウ = 0x0a35;
import savemem  u_char  g_btl_ビャッコ = 0x0a36;
import savemem  u_char  g_btl_スザク = 0x0a37;
import savemem  u_char  g_btl_ガラムサイズ帝国兵 = 0x0a3a;
import savemem  u_char  g_btl_ウォースラ = 0x0a3b;
import savemem  u_char  g_btl_ジャッジ・ベルガ = 0x0a3c;
import savemem  u_char  g_com_debug_flag = 0x0a71;
import savemem  short   battle_global_flag[64] = 0x0a72;

actor 制御(0)
{

	//======================================================================
	//                   Local (actor) scope variables                    //
	//======================================================================
	int     local_var_s0f0_1;        // pos: 0x00;
	int     local_var_s0f0_2;        // pos: 0x40;
	int     local_var_s0f0_3;        // pos: 0x80;
	int     local_var_s0f0_4;        // pos: 0xc0;
	int     local_var_s0f0_5;        // pos: 0x10;

	function init()
	{
		g_com_debug_flag = 0;
		switch (シナリオフラグ)
		{
			case lte(85):
				g_com_会話段階変数 = 0;
				break;
			case lte(0x0157):
				g_com_会話段階変数 = 1;
				break;
			case lte(0x049c):
				g_com_会話段階変数 = 2;
				break;
			case lte(0x05f0):
				g_com_会話段階変数 = 3;
				break;
			case lte(0x07b2):
				g_com_会話段階変数 = 4;
				break;
			case lte(0x0bb8):
				g_com_会話段階変数 = 5;
				break;
			case lte(0x1004):
				g_com_会話段階変数 = 6;
				break;
			case lte(0x13ec):
				g_com_会話段階変数 = 7;
				break;
			case lte(0x17d4):
				g_com_会話段階変数 = 8;
				break;
			default:
				g_com_会話段階変数 = 9;
				break;
		}
		battle_global_flag[0] = ((((((((((((haveitem(0xc012) + haveitem(0xc013)) + haveitem(0xc014)) + haveitem(0xc015)) + haveitem(0xc016)) + haveitem(0xc017)) + haveitem(0xc018)) + haveitem(0xc019)) + haveitem(0xc01a)) + haveitem(0xc01b)) + haveitem(0xc01c)) + haveitem(0xc01d)) + haveitem(0xc01e));
		if (((シナリオフラグ >= 0x1004 && (0xa00000 | getquestscenarioflag(160)) >= 0xa00064) && (0xa00000 | getquestscenarioflag(160)) < 0xa00078))
		{
			setquestscenarioflag(160, 120);
		}
		switch (シナリオフラグ)
		{
			case lt(100):
				g_iw_ラバナスタ通行止め状態 = 2;
				break;
			default:
				g_iw_ラバナスタ通行止め状態 = 0;
				break;
		}
		switch (シナリオフラグ)
		{
			case lt(27):
				global_flag[3] = 0;
				break;
			case lte(0x0122):
				global_flag[3] = 1;
				break;
			case lte(0x0157):
				global_flag[3] = 2;
				break;
			case lte(0x049c):
				global_flag[3] = 3;
				break;
			case lte(0x05f0):
				global_flag[3] = 4;
				break;
			case lte(0x07b2):
				global_flag[3] = 5;
				break;
			case lte(0x0bb8):
				global_flag[3] = 6;
				break;
			case lte(0x1004):
				global_flag[3] = 7;
				break;
			case lte(0x13ec):
				global_flag[3] = 8;
				break;
			case lte(0x17d4):
				global_flag[3] = 9;
				break;
			default:
				global_flag[3] = 10;
				break;
		}
		switch (シナリオフラグ)
		{
			case lt(220):
				g_com_パーティ人数変数 = 0;
				break;
			case lte(249):
				g_com_パーティ人数変数 = 2;
				break;
			case lte(0x0122):
				g_com_パーティ人数変数 = 3;
				break;
			default:
				g_com_パーティ人数変数 = 2;
				break;
		}
		if (g_btl_ガラムサイズ帝国兵 == 0)
		{
			g_btl_ガラムサイズ帝国兵 = 1;
		}
		if (g_btl_プリン == 0)
		{
			g_btl_プリン = 1;
		}
		if (g_btl_ブッシュファイア == 0)
		{
			g_btl_ブッシュファイア = 1;
		}
		if (g_btl_ザコジャッジ == 0)
		{
			g_btl_ザコジャッジ = 1;
		}
		if ((シナリオフラグ >= 0x03e8 && シナリオフラグ < 0x03fa))
		{
			if (g_btl_ギース == 0)
			{
				g_btl_ギース = 1;
			}
		}
		if (g_btl_バッガモナン == 0)
		{
			g_btl_バッガモナン = 1;
		}
		if (g_btl_デモンズウォールA == 0)
		{
			g_btl_デモンズウォールA = 1;
		}
		if (g_btl_デモンズウォールB == 0)
		{
			g_btl_デモンズウォールB = 1;
		}
		if (g_btl_ウォースラ == 0)
		{
			g_btl_ウォースラ = 1;
		}
		if (g_btl_シドドラクロア == 0)
		{
			g_btl_シドドラクロア = 1;
		}
		if (g_btl_ベリアス == 0)
		{
			g_btl_ベリアス = 1;
		}
		if (g_btl_カオス == 0)
		{
			g_btl_カオス = 1;
		}
		if (g_btl_ガブラスバハムート == 0)
		{
			g_btl_ガブラスバハムート = 1;
		}
		if (g_btl_ヴェイン == 0)
		{
			g_btl_ヴェイン = 1;
		}
		if (g_btl_ラスト・ヴェイン == 0)
		{
			g_btl_ラスト・ヴェイン = 1;
		}
		if (g_btl_ジャッジ・ベルガ == 0)
		{
			g_btl_ジャッジ・ベルガ = 1;
		}
		if (g_btl_ゾディアーク == 0)
		{
			g_btl_ゾディアーク = 1;
		}
		if (g_btl_キュクレイン == 0)
		{
			g_btl_キュクレイン = 1;
		}
		if (g_btl_ザルエラ == 0)
		{
			g_btl_ザルエラ = 1;
		}
		if (g_btl_ハシュマリム == 0)
		{
			g_btl_ハシュマリム = 1;
		}
		if (g_btl_シュミハザ == 0)
		{
			g_btl_シュミハザ = 1;
		}
		if (g_btl_マティウス == 0)
		{
			g_btl_マティウス = 1;
		}
		if (g_btl_ゼロムス == 0)
		{
			g_btl_ゼロムス = 1;
		}
		if (g_btl_エクスデス == 0)
		{
			g_btl_エクスデス = 1;
		}
		if (g_btl_アルテマ == 0)
		{
			g_btl_アルテマ = 1;
		}
		if (g_btl_アドラメレク == 0)
		{
			g_btl_アドラメレク = 1;
		}
		if (g_btl_ボムキング == 0)
		{
			g_btl_ボムキング = 1;
		}
		if (g_btl_魔神竜 == 0)
		{
			g_btl_魔神竜 = 1;
		}
		if (g_btl_ミミッククイーン == 0)
		{
			g_btl_ミミッククイーン = 1;
		}
		if (g_btl_ハイドラ == 0)
		{
			g_btl_ハイドラ = 1;
		}
		if (g_btl_ティアマット == 0)
		{
			g_btl_ティアマット = 1;
		}
		if (g_btl_ダイダロス == 0)
		{
			g_btl_ダイダロス = 1;
		}
		if (g_btl_タイラント == 0)
		{
			g_btl_タイラント = 1;
		}
		if (g_btl_メタルドラゴン == 0)
		{
			g_btl_メタルドラゴン = 1;
		}
		if (g_btl_アーリマン == 0)
		{
			g_btl_アーリマン = 1;
		}
		if (g_btl_ラフレシア == 0)
		{
			g_btl_ラフレシア = 1;
		}
		if (g_btl_マンドラゴラーズ == 0)
		{
			g_btl_マンドラゴラーズ = 1;
		}
		if (g_btl_フンババ == 0)
		{
			g_btl_フンババ = 1;
		}
		if (g_btl_フューリー == 0)
		{
			g_btl_フューリー = 1;
		}
		if (g_btl_エルダードラゴン == 0)
		{
			g_btl_エルダードラゴン = 1;
		}
		if (g_btl_ゲンブ == 0)
		{
			g_btl_ゲンブ = 1;
		}
		if (g_btl_ビャッコ == 0)
		{
			g_btl_ビャッコ = 1;
		}
		if (g_btl_セイリュウ == 0)
		{
			g_btl_セイリュウ = 1;
		}
		if (g_btl_スザク == 0)
		{
			g_btl_スザク = 1;
		}
		if (g_btl_クリスタルミミックグリーン == 0)
		{
			g_btl_クリスタルミミックグリーン = 1;
		}
		if (g_btl_クリスタルミミックブルー == 0)
		{
			g_btl_クリスタルミミックブルー = 1;
		}
		if (g_btl_クリスタルミミックレッド == 0)
		{
			g_btl_クリスタルミミックレッド = 1;
		}
		switch (シナリオフラグ)
		{
			case lt(105):
				setsaveramtelepostatus(0);
				break;
			case lt(190):
				setsaveramtelepostatus(1);
				break;
			case lt(0x0157):
				setsaveramtelepostatus(0);
				break;
			case lt(0x0208):
				setsaveramtelepostatus(1);
				break;
			case lt(0x029e):
				setsaveramtelepostatus(0);
				break;
			case lt(0x02f8):
				setsaveramtelepostatus(1);
				break;
			case lt(0x0442):
				setsaveramtelepostatus(0);
				break;
			case lt(0x056e):
				setsaveramtelepostatus(1);
				break;
			case lt(0x062c):
				setsaveramtelepostatus(0);
				break;
			case lt(0x0898):
				setsaveramtelepostatus(1);
				break;
			case lt(0x0d16):
				setsaveramtelepostatus(1);
				break;
			case lt(0x0fa0):
				setsaveramtelepostatus(0);
				break;
			case lt(0x1518):
				setsaveramtelepostatus(1);
				break;
			case lt(0x17d4):
				setsaveramtelepostatus(0);
				break;
			case lt(0x1806):
				setsaveramtelepostatus(1);
				break;
			case lt(0x2af8):
				setsaveramtelepostatus(0);
				break;
			default:
				setsaveramtelepostatus(1);
				break;
		}
		g_com_e3_flag = 0;
		// Keep the equippable 0x1182 in step with key item 0x80ac (inventory check only; the original also counted
		// equipped copies through a large save-memory array the unmodded game rejects).
		if ((haveitem(0x80ac) && !(haveitem(0x1182))))
		{
			additem_47f(0x1182, 1, 0);
		}
		else if ((!(haveitem(0x80ac)) && haveitem(0x1182)))
		{
			takeoff_equip(0x1182);
			setitemnum(0x1182, 0);
		}
		// Keep the equippable 0x117e in step with key item 0x8089 (inventory check only; the original also counted
		// equipped copies through a large save-memory array the unmodded game rejects).
		if ((haveitem(0x8089) && !(haveitem(0x117e))))
		{
			additem_47f(0x117e, 1, 0);
		}
		else if ((!(haveitem(0x8089)) && haveitem(0x117e)))
		{
			takeoff_equip(0x117e);
			setitemnum(0x117e, 0);
		}
		// Keep the equippable 0x1183 in step with key item 0x8078 (inventory check only; the original also counted
		// equipped copies through a large save-memory array the unmodded game rejects).
		if ((haveitem(0x8078) && !(haveitem(0x1183))))
		{
			additem_47f(0x1183, 1, 0);
		}
		else if ((!(haveitem(0x8078)) && haveitem(0x1183)))
		{
			takeoff_equip(0x1183);
			setitemnum(0x1183, 0);
		}
		// Keep the equippable 0x10a3 in step with key item 0x808b (inventory check only; the original also counted
		// equipped copies through a large save-memory array the unmodded game rejects).
		if ((haveitem(0x808b) && !(haveitem(0x10a3))))
		{
			additem_47f(0x10a3, 1, 0);
		}
		else if ((!(haveitem(0x808b)) && haveitem(0x10a3)))
		{
			takeoff_equip(0x10a3);
			setitemnum(0x10a3, 0);
		}
		// Keep the equippable 0x10a2 in step with key item 0x808c (inventory check only; the original also counted
		// equipped copies through a large save-memory array the unmodded game rejects).
		if ((haveitem(0x808c) && !(haveitem(0x10a2))))
		{
			additem_47f(0x10a2, 1, 0);
		}
		else if ((!(haveitem(0x808c)) && haveitem(0x10a2)))
		{
			takeoff_equip(0x10a2);
			setitemnum(0x10a2, 0);
		}
		eventdispose();
		return;
	}
}

//======================================================================
//                          Map exit arrays                           //
//======================================================================

mapExitArray mapExitGroup0[0] = {

};

mapExitArray mapExitGroup1[0] = {

};

mapExitArray mapExitGroup2[0] = {

};

mapExitArray mapExitGroup3[0] = {

};

mapExitArray mapExitGroup4[0] = {

};
