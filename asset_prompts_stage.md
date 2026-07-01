# ステージ背景 画像生成プロンプト

対戦ステージの背景（`Resources/Stage/` に入れる大きな背景スプライト）用のプロンプト集。
浮遊する石の対戦台（`platform.png`＝ダークな石材＋シアンのネオン発光ルーン＋装飾金属＋下部に青いクリスタル）の
雰囲気に合わせた、ダークファンタジー×アーケードの世界観で統一する。

## 最重要：構図ルール（吹っ飛び・KOの見切れ対策）

ゲームのカメラは背景スプライトの範囲内にクランプされる（画像の外＝黒を映さない）。
そのためキャラが画像の上端より上に打ち上げられると見切れる。これを防ぐため、
**地面（キャラが立つ床面）を画像の下側に置き、上側を大きく空ける**構図で生成する。

- **アスペクト比：16:9 の横長**（例 1792×1024 / 1536×864 など、生成器で選べる最も横長のもの）。
- **床面（地平線・キャラが立つ面）は画像の下から約25〜30%の高さ**に置く。
  → つまり **下30%＝プレイ床面まわり、上70%＝空・大気（吹っ飛び用の縦余白）**。
- 上70%は空／天／異空間などの**開けた背景**にし、キャラの真上に大きな建造物や
  ごちゃついた要素を置かない（打ち上げ時にキャラが埋もれないように）。
- **左右の端15%は装飾（柱・岩・幟など）を置いてよいが、中央70%は開けておく**
  （場外方向のふっとび余白）。
- キャラが立つ床の帯（下25〜30%〜中央付近）は**中〜暗トーンで情報量を控えめ**にし、
  発光する派手なキャラが手前で映えるようにする（床にギラつく強い光やコントラストを置かない）。
- 真横視点（サイドビュー格闘ゲーム）向けの背景。**極端なパース（床が手前に倒れ込む俯瞰）は避け**、
  ほぼ水平の地平線＋奥行きは大気遠近で表現する。
- **キャラクター・人物・UI・文字・ロゴ・枠・ウォーターマークは一切入れない。**

## 配色

- 主役：ダークグレー〜黒の石材、鉄・青銅の装飾。
- アクセント発光：**シアン〜エレクトリックブルー（台と同じ 1P 青）**。
- 差し色：ごく控えめにゴールド（装飾）＋わずかな赤（2P 色を思わせる幟・炎など、左右対称に少量）。
- 全体は少し暗め・彩度控えめで、手前のキャラが浮き立つように。

---

## 変種A：雲海に浮かぶ古代アリーナ（推奨・デフォルト）

```
Wide 16:9 side-scrolling 2D fighting game background, no characters.
An ancient arcane stone arena floating high above a sea of clouds at dusk.
The stone floor (weathered dark cracked flagstones with glowing cyan runes and
inlaid electric-blue crystals, ornate dark-iron filigree edges matching a fantasy
floating platform) sits in the LOWER 25-30% of the frame, nearly horizontal.
Above it, the UPPER 70% is a vast open twilight sky: layered clouds, soft god-rays,
distant floating rock islands and glowing blue crystal spires far in the background,
faint aurora. Symmetrical composition, tall open headroom, uncluttered center.
Dark, slightly desaturated, cinematic, high-detail digital painting, atmospheric depth.
Foreground floor kept mid-to-dark and low-contrast so bright characters read clearly.
No people, no UI, no text, no watermark, no logo, no border.
```

## 変種B：巨大クリスタル洞窟の神殿

```
Wide 16:9 side-scrolling 2D fighting game background, no characters.
A colossal underground temple hall inside a crystal cavern.
The floor (polished dark obsidian with glowing cyan magic circles and blue crystal
inlays, ornate bronze temple trim, matching a floating arcane platform) sits in the
LOWER 25-30% of the frame, nearly horizontal. The UPPER 70% opens into a towering
cavern: massive glowing electric-blue crystal clusters, ancient stone pillars fading
into darkness, drifting light particles and soft volumetric haze. Symmetrical, open
headroom, uncluttered center. Dark moody lighting, cinematic, high-detail digital
painting. Floor area mid-to-dark, low-contrast for character readability.
No people, no UI, no text, no watermark, no logo, no border.
```

## 変種C：星海に浮かぶヴォイド・コロシアム

```
Wide 16:9 side-scrolling 2D fighting game background, no characters.
An arcane colosseum platform adrift in a starry cosmic void.
The arena floor (dark cracked stone with glowing cyan runes, electric-blue crystal
shards and ornate dark-metal edging, matching a floating fantasy platform) sits in the
LOWER 25-30% of the frame, nearly horizontal. The UPPER 70% is deep space: nebula
clouds in blue and faint magenta, scattered stars, distant shattered floating rock
islands trailing blue crystals, soft cosmic god-rays. Symmetrical, tall open headroom,
uncluttered center. Dark, cinematic, high-detail digital painting, atmospheric.
Floor kept mid-to-dark and low-contrast so foreground characters pop.
No people, no UI, no text, no watermark, no logo, no border.
```

## ネガティブ（対応する生成器なら）

```
characters, people, humans, creatures, UI, HUD, text, letters, logo, watermark,
signature, frame, border, top-down view, heavy floor perspective, cluttered top,
tall structures in center blocking the sky, bright high-contrast floor, low horizon sky cut-off
```

---

## Unity 側の対処（プロンプトと併用）

1. **地面の帯を画像下30%に合わせる**：背景を配置後、床コライダ（キャラのstand Y=-1.8）を
   画像内の床面ラインに合わせる。上70%が空くので打ち上げ時もカメラが上へ追従できる。
2. **場外インジケータ（虫眼鏡／矢印）**：それでも画面外へ出た時のため、生存キャラが
   ビューポート外に出たら画面端に方向マーカー＋小さな顔アイコンを出す仕組みを追加する
   （実装可能。指示があれば対応）。
