# エフェクト画像 生成プロンプト集

このゲームのエフェクト画像は `aismash_unity/Assets/Resources/Effects/*.png`（一部 `Stage/`）に置き、
**フラットなクロマキー緑（#00FF00）背景**を Unity 側（`WhiteBackgroundRemover.ApplyChromaGreen`）で透過します。
そのため、**背景・抜きたい余白はすべて #00FF00 のべた塗り緑**にしてください（影・グラデ背景・枠は付けない）。

画像生成は別途行う前提。以下の英語プロンプトをそのまま貼り付けて使えます。

## 共通スタイル（各プロンプトに含める）

- **STYLE**: `2D anime arcade fighting-game VFX, hand-painted game asset, bold clean ink outlines, highly saturated energetic colors, strong rim glow and bloom, dynamic and punchy, centered and fully inside the frame`
- **BG**: `flat chroma key green background (#00FF00), the entire background and any empty/hollow areas are pure solid green #00FF00, no shadow, no gradient background, no border, no watermark`
- 純粋なエフェクト（文字・キャラなし）には `no text, no letters, no character figure` も付ける。
- 配色の基準: **1P=ブルー（#2DB4FF系）/ 2P=レッド（#FF3D3A系）/ アクセント=ゴールド（#FFC72E）/ 危険=レッド**。
- 推奨は正方形 1024×1024（バナー系は 1280×720）。最終的に Unity 側でスケールするのでアスペクトが主。

---

## ① ガードバリア（耐久値に比例して3段階）  ★リクエスト

キャラを包む半透明の力場シールド。**中央は空洞**（中にキャラが立つ）＝中央も #00FF00 緑にして抜く。
コード側でプレイヤー色に薄く着色できるよう、**ベースはシアン〜白の中立色**にしてください。
3段階用意（ボールの `ball / ball_crack1 / ball_crack2` と同じ考え方）。

- 推奨サイズ: 1024×1024（正方形）
- ファイル名: `Effects/guard_barrier_full.png` / `Effects/guard_barrier_mid.png` / `Effects/guard_barrier_low.png`

**full（耐久満タン）**
```
A translucent hexagonal energy force-field shield dome forming a protective bubble, the center is hollow and empty (pure solid green #00FF00 so it becomes transparent), bright cyan-and-white glowing hex panels, crisp glowing circular rim, intact and strong sci-fi barrier, faint inner light, 2D anime arcade fighting-game VFX, hand-painted game asset, bold clean ink outlines, strong rim glow and bloom, no character, no text, no letters, flat chroma key green background (#00FF00), the entire background and the hollow center are pure solid green #00FF00, no shadow, no border, no watermark
```

**mid（半分）**
```
The same translucent hexagonal energy shield bubble but weakened: flickering, a few glowing cracks running across the hex panels, dimmer cyan with faint yellow stress lines and small sparks, the center hollow and empty (pure solid green #00FF00), 2D anime arcade fighting-game VFX, bold clean ink outlines, no character, no text, no letters, flat chroma key green background (#00FF00), hollow center is pure solid green #00FF00, no shadow, no border, no watermark
```

**low（残りわずか・破壊寸前）**
```
The same hexagonal energy shield bubble nearly broken: heavily cracked and shattering hex panels, unstable and dim, red-orange warning glow, glass shards breaking off, sparks, the center hollow and empty (pure solid green #00FF00), 2D anime arcade fighting-game VFX, bold clean ink outlines, no character, no text, no letters, flat chroma key green background (#00FF00), hollow center is pure solid green #00FF00, no shadow, no border, no watermark
```

---

## ② カウントダウン 3 / 2 / 1 / GO（4枚）  ★リクエスト

巨大なアーケード風の数字＋GO。**文字自体が主役**なので `no text` は付けない。太字イタリック。

- 推奨サイズ: 1024×1024（または 1280×720）
- ファイル名: `Effects/count_3.png` / `count_2.png` / `count_1.png` / `count_go.png`

**3（ブルー）**
```
A huge stylized numeral "3", bold italic chrome-and-neon arcade fighting-game style, electric blue (#2DB4FF) with a bright white core, glossy beveled metal edges, cyan energy burst and radiating comic speed lines behind it, glowing outline, the number is the only element and fills most of the frame, 2D anime arcade fighting-game VFX, strong rim glow and bloom, flat chroma key green background (#00FF00), pure solid green background, no extra text, no watermark, no border, no shadow
```

**2（ゴールド）**
```
A huge stylized numeral "2", bold italic chrome-and-neon arcade fighting-game style, gold and amber (#FFC72E) with white core, glossy beveled edges, orange spark burst and speed lines behind it, glowing outline, fills most of the frame, 2D anime arcade fighting-game VFX, strong rim glow and bloom, flat chroma key green background (#00FF00), pure solid green background, no extra text, no watermark, no border, no shadow
```

**1（レッド）**
```
A huge stylized numeral "1", bold italic chrome-and-neon arcade fighting-game style, hot red (#FF3D2A) with white core, glossy beveled edges, fiery burst and urgent speed lines behind it, intense glowing outline, fills most of the frame, 2D anime arcade fighting-game VFX, strong rim glow and bloom, flat chroma key green background (#00FF00), pure solid green background, no extra text, no watermark, no border, no shadow
```

**GO!（発進）**
```
The word "GO!" in huge bold italic arcade fighting-game lettering, vivid green-to-gold gradient with white core and thick glossy outline, explosive radial speed lines and motion blur behind it, exciting and energetic, fills most of the frame, 2D anime arcade fighting-game VFX, strong rim glow and bloom, flat chroma key green background (#00FF00), pure solid green background, no other text, no watermark, no border, no shadow
```

---

## ③ KO 画像  ★リクエスト

決着の「K.O.」スタンプ。文字が主役。

- 推奨サイズ: 1280×720（横長バナー）
- ファイル名: `Effects/ko.png`

```
A huge bold italic "K.O." stamp in arcade fighting-game style, cracked heavy impact lettering, white and gold with a thick black outline, a red shockwave and white glass-crack burst exploding behind it, dramatic knockout banner, slight motion blur, fills most of the frame, 2D anime arcade fighting-game VFX, strong rim glow and bloom, flat chroma key green background (#00FF00), pure solid green background, no other text, no watermark, no border, no shadow
```

---

## ④ 空中ジャンプ エフェクト（再生成）  ★リクエスト

既存 `Effects/jump_air.png` を差し替え。足元に出る、空中で踏み切る輪。キャラなし。

- 推奨サイズ: 512×512
- ファイル名: `Effects/jump_air.png`（上書き）

```
A midair double-jump burst effect: a flat horizontal ring of white and cyan wind energy with a soft puff of swirling air and a few sparkle stars, seen slightly from above as if under the feet, crisp glowing rim, sense of an upward push, 2D anime arcade fighting-game VFX, bold clean ink outlines, strong rim glow and bloom, no character, no text, no letters, flat chroma key green background (#00FF00), pure solid green background, no shadow, no border, no watermark
```

---

# 追加で増やすと良いアセット（ボーナス）

「とにかく増やしたい」とのことなので、ゲーム内で使い所の多い汎用エフェクトを提案します。
（※②④以外は表示するためのコード対応が別途必要なものがあります。希望のものは生成後に組み込みます。）

| 用途 | ファイル名 | 推奨サイズ |
|---|---|---|
| 地上ジャンプ踏切 | `Effects/jump_ground.png`（差し替え可） | 512×512 |
| 砂埃（ダッシュ/着地） | `Effects/dust.png`（差し替え可） | 256×256 |
| ヒット火花 | `Effects/hit_spark.png` | 512×512 |
| 斬撃トレイル | `Effects/slash.png` | 768×512 |
| 着地/重撃の衝撃波 | `Effects/shockwave.png` | 1024×1024 |
| ガードブレイク | `Effects/guard_break.png` | 1024×1024 |
| スマッシュ溜めオーラ | `Effects/smash_charge.png` | 768×768 |
| 強化バフ（上昇） | `Effects/buff_up.png` | 512×768 |
| 弱体デバフ（下降） | `Effects/debuff_down.png` | 512×768 |
| 炎/バーン オーラ | `Effects/aura_burn.png` | 512×768 |
| 氷/凍結 オーラ | `Effects/aura_freeze.png` | 512×768 |
| スタン（星回転） | `Effects/aura_stun.png` | 512×256 |
| スロー オーラ | `Effects/aura_slow.png` | 512×768 |
| 反射バリア | `Effects/reflect.png` | 1024×1024 |
| カウンター閃光 | `Effects/counter.png` | 768×768 |
| 勝利バナー | `Effects/win.png` | 1280×720 |

**地上ジャンプ踏切**
```
A ground takeoff burst: a flat dust ring kicked up from the floor with upward wind streaks and a few small pebbles, warm tan-white dust mixed with faint cyan energy, seen slightly from above, 2D anime arcade fighting-game VFX, bold clean ink outlines, no character, no text, no letters, flat chroma key green background (#00FF00), pure solid green background, no shadow, no border, no watermark
```

**砂埃（汎用）**
```
A small soft puff of dust cloud, light tan and grey, rounded billowing shape, simple and readable, for dash and landing, 2D anime game VFX, soft edges, no character, no text, no letters, flat chroma key green background (#00FF00), pure solid green background, no shadow, no border, no watermark
```

**ヒット火花**
```
A sharp star-shaped impact flash, white-yellow core with orange spikes radiating outward, a bright pop of energy at the moment of a hit, crisp glowing edges, 2D anime arcade fighting-game VFX, no character, no text, no letters, flat chroma key green background (#00FF00), pure solid green background, no shadow, no border, no watermark
```

**斬撃トレイル**
```
A single crescent-shaped sword slash arc, white-cyan glowing trail with soft motion blur and a thin bright leading edge, diagonal sweep, 2D anime arcade fighting-game VFX, bold clean lines, no character, no text, no letters, flat chroma key green background (#00FF00), pure solid green background, no shadow, no border, no watermark
```

**衝撃波**
```
An expanding flat ground shockwave ring, white and blue energy with a crisp glowing rim and faint dust, seen slightly from above, sense of a heavy impact spreading outward, 2D anime arcade fighting-game VFX, no character, no text, no letters, flat chroma key green background (#00FF00), pure solid green background, no shadow, no border, no watermark
```

**ガードブレイク**
```
A shattering shield burst: hexagonal glass shards exploding outward with a red flash and white sparks, broken energy panels flying apart, sense of a guard being broken, 2D anime arcade fighting-game VFX, strong glow, no character, no text, no letters, flat chroma key green background (#00FF00), pure solid green background, no shadow, no border, no watermark
```

**スマッシュ溜めオーラ**
```
A building charge aura: concentric swirling energy converging inward, yellow to orange with rising sparks and small lightning arcs, tense pre-attack glow, vertical column shape with a hollow center (pure solid green #00FF00), 2D anime arcade fighting-game VFX, strong rim glow, no character, no text, no letters, flat chroma key green background (#00FF00), hollow center is pure solid green #00FF00, no shadow, no border, no watermark
```

**強化バフ（上昇）**
```
A positive power-up aura: a vertical column of rising golden sparkles and upward light streaks, warm and uplifting, hollow center (pure solid green #00FF00), 2D anime arcade fighting-game VFX, soft glow, no character, no text, no letters, flat chroma key green background (#00FF00), hollow center is pure solid green #00FF00, no shadow, no border, no watermark
```

**弱体デバフ（下降）**
```
A negative debuff aura: heavy purple smoke and downward-falling dark particles with droopy downward arrows of energy, gloomy and sluggish, hollow center (pure solid green #00FF00), 2D anime arcade fighting-game VFX, no character, no text, no letters, flat chroma key green background (#00FF00), hollow center is pure solid green #00FF00, no shadow, no border, no watermark
```

**炎/バーン オーラ**
```
A flickering fire aura wrapping a body shape, orange and red flames with yellow core licking upward, hot and lively, hollow center (pure solid green #00FF00), 2D anime arcade fighting-game VFX, bold flame shapes, no character, no text, no letters, flat chroma key green background (#00FF00), hollow center is pure solid green #00FF00, no shadow, no border, no watermark
```

**氷/凍結 オーラ**
```
A frost encasement aura: pale blue ice crystals and sharp frost shards forming around a hollow center (pure solid green #00FF00), cold mist, glassy highlights, 2D anime arcade fighting-game VFX, no character, no text, no letters, flat chroma key green background (#00FF00), hollow center is pure solid green #00FF00, no shadow, no border, no watermark
```

**スタン（星回転）**
```
A ring of spinning cartoon stars mixed with small yellow lightning sparks, arranged in a horizontal halo to float above a head, dizzy stun effect, 2D anime arcade fighting-game VFX, bold clean outlines, no character, no text, no letters, flat chroma key green background (#00FF00), pure solid green background, no shadow, no border, no watermark
```

**スロー オーラ**
```
A sluggish slow aura: heavy blue translucent droplets and drifting downward ripples with faint chains of light, weighed-down feeling, hollow center (pure solid green #00FF00), 2D anime arcade fighting-game VFX, no character, no text, no letters, flat chroma key green background (#00FF00), hollow center is pure solid green #00FF00, no shadow, no border, no watermark
```

**反射バリア**
```
A hexagonal mirror bubble flash, prismatic translucent panels with a bright rainbow rim reflecting light, a brief protective dome, hollow center (pure solid green #00FF00), 2D anime arcade fighting-game VFX, strong glossy glow, no character, no text, no letters, flat chroma key green background (#00FF00), hollow center is pure solid green #00FF00, no shadow, no border, no watermark
```

**カウンター閃光**
```
A sharp white counter flash: a bright crossing gleam with two crossed slash lines and a burst of sparks at the moment of a counter, crisp and snappy, 2D anime arcade fighting-game VFX, no character, no text, no letters, flat chroma key green background (#00FF00), pure solid green background, no shadow, no border, no watermark
```

**勝利バナー**
```
The word "WIN" in big bold italic arcade fighting-game lettering, gold and white with a thick dark outline, sparkling confetti and radiant light rays behind it, celebratory victory banner, 2D anime arcade fighting-game VFX, strong glow, flat chroma key green background (#00FF00), pure solid green background, no other text, no watermark, no border, no shadow
```

---

## 生成・配置メモ

- すべて **背景＋空洞部は #00FF00 のべた塗り緑**にすること（半透明シールドの内側など、抜きたい所も緑）。
- 生成後は `aismash_unity/Assets/Resources/Effects/` に上記ファイル名で配置（既存の `jump_air.png` 等は上書き）。
- カウントダウン（②）・KO（③）・ガードバリア（①）・各種オーラは、表示・耐久連動・差し替えのための**コード対応が別途必要**。生成が揃ったら組み込みます（どれを使うか教えてください）。
- 空中ジャンプ（④）・地上ジャンプ・砂埃は既存の読み込み先をそのまま使うため、差し替えるだけで反映されます。
