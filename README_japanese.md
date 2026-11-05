# QuestSaturationExperiment

Full-Field Color Manipulation Library for Meta Quest 3

Author: Ryo Matsuzaki
License: MIT

Meta Quest 3 のパススルー映像全体の明るさ・コントラスト・彩度をリアルタイムに変える Unity プロジェクトです。`OVRPassthroughLayer.SetBrightnessContrastSaturation` を使います。値はそれぞれ [-1, +1] で、0 が「変化なし」、彩度 -1 がモノクロです。

モードは2つあります。

- **実験（Experiment）**：3条件（無変化 / 強い減彩度 / 強い過彩度）の課題です。手順とタイミングは 0.2.0 と同じです。
- **FreeControl（オペレーター用）**：明るさ・コントラスト・彩度を手で設定するか、彩度を波形で自動的に変化させます。

どのセッションも、ヘッドセット内の CSV ログに記録されます。

[English README](README.md)

## 動作環境

Unity 6000.3.7f1 / URP 17.3 / Meta XR SDK 201.0.0 / OpenXR 1.16 / Meta Quest 3（Android ビルド）。コントローラーが必要です（ハンドトラッキングは無効にしています）。

## 実験

1. **Selection**：視界が白くなり、球が3つ表示されます。右コントローラーで狙い、**左右どちらかの人差し指トリガー**で決定します。レイは常に右手から出ます。どちらのトリガーで決めたかはログに残ります。
2. **Trial**：パススルーが表示されます。彩度は 3 秒間 0 のまま、その後 10 秒かけて目標値まで直線的に変わり、以降は目標値を保ちます。時間は球を決定したフレームから計ります。
3. **右A** ボタンを 2 秒長押しすると試行を終えて Selection に戻ります。

| 条件 | 目標彩度 |
|---|---|
| A | 0.0 |
| B | -0.8 |
| C | +0.8 |

この設定はコード（`ExperimentProfile.Canonical`）に固定してあり、インスペクターからは変えられません。どのビルドでも同じ手順で実験が行われます。試行中に効くのは、右A 長押しのリセットとマーカー（左グリップ）だけです。他のボタンは無視し、ログに `input_ignored` として記録します。

## オペレーターモード（FreeControl）

Selection 中に **左☰（メニュー）** ボタンを 1.5 秒長押しすると FreeControl に入り、もう一度長押しすると戻ります。試行中には入れません。FreeControl は必ず Manual・停止の状態で始まり、カーソルはいちばん上（Mode）に置かれます。表示は、いまの見え方から 0.5 秒かけて切り替わります。

| ボタン | 操作 |
|---|---|
| 右スティック 上 / 下 | 項目を選ぶ |
| 右スティック 左 / 右 | 値を変える。小数の項目は押し続けるとリピート。Steps と Participant は倒すたびに 1 つずつ |
| 右スティック 押し込み | 選んでいる項目を既定値に戻す |
| 右B（タップ） | 波形の再生 / 一時停止（Manual なら Auto に切り替えて再生） |
| 右B（1 秒長押し） | 停止して先頭に戻す |
| 左Y | パネルの表示 / 非表示 |
| 左X | パネルを目の前に呼び戻す |
| 左グリップ | ログにマーカーを入れる（どのモードでも有効。試行中は振動しない） |
| 左☰（1.5 秒長押し） | Selection に戻る |

| 項目 | 範囲 | 刻み | 既定値 |
|---|---|---|---|
| Mode | Manual / Auto | | Manual |
| Saturation | -1 〜 +1 | 0.05 | 0 |
| Brightness | -0.5 〜 +0.5 | 0.05 | 0 |
| Contrast | -1 〜 +1 | 0.05 | 0 |
| Waveform | Sine / Linear / Step / Ramp | | Sine |
| Wave from | -1 〜 +1 | 0.05 | 0 |
| Wave to | -1 〜 +1 | 0.05 | -1 |
| Period s | 1 〜 120 | 0.5 | 10 |
| Steps | 2 〜 20 | 1 | 5 |
| Participant | 0 〜 999 | 1 | 0 |

- **Auto** で変わるのは彩度だけです。明るさとコントラストは Manual の値のままです。Manual に戻すと、彩度も Manual の値に戻ります。
- どの波形も *Wave from* から始まります。Sine・Linear・Step は1周期で *Wave to* まで行って戻ります（Step は1段ずつ上がり、1段ずつ下がります）。Ramp は *Wave from* から *Wave to* へ片道だけ進み、そこで止まります。
- 再生中に周期を変えても、値は飛びません。一時停止は位置を保ち、停止は先頭に戻します。
- 右スティックは、最初に倒した向き（上下か左右）だけを、中央に戻すまで受け付けます。左右に倒したまま斜めを通っても、別の行には移りません。上下と左右を切り替えるときは、いったん中央に戻してください。
- 各項目と被験者 ID はヘッドセットに保存され、次の起動時に戻ります。モードと再生状態は保存しません。
- **光過敏性への注意**：Auto は視野全体を変化させます。使う前に、被験者に光過敏性がないか確認してください。周期は 1 秒より短くできません。

パネルは FreeControl 中だけ表示されます。Selection や試行が始まるときには必ず隠れます。

## セッションログ（CSV）

保存先は `Android/data/com.ryomtzk.questsaturationexperiment/files/sessions/session_<UTC時刻>_P<被験者ID>.csv` です。起動するたびに新しいファイルを作ります。被験者 ID を変えたときも新しいファイルになります。ID を送っている間は、まだ切り替わりません。切り替わるのは、次にほかの項目（Participant 以外）を変えたとき、再生・一時停止・停止をしたとき、マーカーを入れたとき、FreeControl を出たときです。それまでパネルには `P007 -> P008 (pending)` のように表示されます。その前に元の ID に戻せば、何も変わりません。新しい被験者の試行番号は 1 から数えます。この起動中に使った ID に戻ったときは、その続きから数えます。アプリを起動し直すと 1 からになるので、ファイルの区別には `session_id` を使ってください。

- サンプル行：試行中は毎フレーム、それ以外は毎秒 10 行。1 秒ごとにディスクへ書き出します。
- イベント行：その場でディスクへ書き出します。アプリが異常終了しても、失われるサンプルは最大でおよそ 1 秒分です。

| 列 | 内容 |
|---|---|
| `session_id`, `participant_id` | セッション（開始時刻 UTC と被験者 ID）と被験者 ID |
| `wall_clock_utc` | UTC 時刻（ISO 8601） |
| `t_realtime_s`, `t_game_s`, `frame`, `unscaled_dt` | 起動からの時間、`Time.time`、フレーム番号、フレーム時間 |
| `state` | `Selection` / `Trial` / `FreeControl` |
| `trial_index`, `condition` | その被験者の試行番号（1 から。上の説明を参照）と条件 |
| `trial_stage`, `trial_elapsed_s`, `target_saturation` | `hold` / `ramp` / `target`、カーブに渡した経過秒、条件の目標値 |
| `passthrough_visible` | レイヤーが有効で、有効にしてから描画が始まっていれば 1 |
| `saturation`, `brightness`, `contrast` | レイヤーに実際に送った値（範囲内に収めた後） |
| `free_mode`, `waveform`, `playing` | FreeControl の状態 |
| `active_controller` | `OVRInput.GetActiveController()` |
| `event`, `detail` | イベント名と `key=value;...` 形式の詳細（サンプル行では空） |

イベント：`session_start`（アプリのバージョン、git commit、機種、リフレッシュレート、タイムゾーン、実験プロファイル）、`state_change`、`condition_selected`（球の位置、マテリアル、押したトリガー）、`trial_start`、`layer_enabled`、`passthrough_resumed`（有効にしてから映るまでの時間）、`passthrough_not_visible_at_ramp_start`、`passthrough_resume_timeout`、`ramp_start`、`ramp_end`、`trial_end`（終わった理由と、途中で中断があったか）、`marker`、`play` / `pause` / `stop` / `param_change`、`hmd_mounted` / `hmd_unmounted`、`input_focus_lost` / `input_focus_acquired`、`app_pause` / `app_resume`、`tracking_lost` / `tracking_acquired`、`recentered`、`active_controller_changed`、`input_ignored`、`session_end`。

### ログをパソコンにコピーする

**アプリをアンインストールする前に、必ずログをコピーしてください。アンインストールするとログも消えます。**（`adb install -r` での上書きインストールなら残ります。）

```bash
tools/pull_sessions.sh
```

`./quest_sessions/<日時>/` にすべてのセッションファイルをコピーします。スクリプトを使わない場合は次のとおりです。

```bash
adb pull /sdcard/Android/data/com.ryomtzk.questsaturationexperiment/files/sessions ./quest_sessions
```

イベントは `[QSE]` という接頭辞付きで logcat にも出ます。

```bash
adb logcat -s Unity | grep QSE
```

## フォルダ構成

- `Assets/Scripts/Core`：OVR に依存しない純粋なロジック（実験プロファイルと進行、波形、FreeControl のモデル、入力の規則、設定と CSV の書式）。EditMode テストで確認しています。
- `Assets/Scripts/Runtime`：Core を OVR とシーンにつなぐ MonoBehaviour（`ExperimentManager`、`SaturationController`、`ConditionSelector`、`SphereOption`、`FreeControlController`、`SettingsStore`、`SessionLogger`、`OperatorPanel`、`ControllerShortcuts`）。
- `Assets/Scripts/Editor`：**QuestSaturation** メニューの Setup Scene、Validate Scene、Build Quest APK。
- `Assets/Tests/EditMode`：テスト（Window > General > Test Runner > EditMode）。

## セットアップとビルド

1. Unity 6000.3.7f1 でプロジェクトを開き、パッケージの解決を待ちます。
2. `Assets/Scenes/PassthroughScene.unity` を開き、ビルドターゲットを Android にします。
3. **QuestSaturation > Validate Scene** を実行します。問題が表示されたら **QuestSaturation > Setup Scene** を実行します。
4. **QuestSaturation > Build Quest APK** で `~/Downloads/QuestBuilds/QuestSaturationExperiment_operator.apk` にビルドします。インストールはしません。同名の APK がある場合は上書きせず、新しいほうに日時を付けます。
5. `adb install -r <apk>` でインストールします。

エディタではパススルーを再生できないため、動作は実機で確認してください。

コマンドラインから実行する場合（`UNITY` は Unity 6000.3.7f1 の実行ファイル）：

```bash
"$UNITY" -batchmode -nographics -projectPath . -buildTarget Android -runTests -testPlatform EditMode -testResults TestResults.xml
"$UNITY" -batchmode -nographics -projectPath . -buildTarget Android -executeMethod QuestSaturation.Editor.SceneValidator.RunBatch -quit
"$UNITY" -batchmode -nographics -projectPath . -buildTarget Android -executeMethod QuestSaturation.Editor.BuildScript.BuildQuestApk -quit
```

## 補足

- SDK がパススルーの色設定を毎フレーム上書きすることはありません（以前の README には Building Blocks が上書きすると書いていましたが、誤りでした）。実際の問題は、旧コードがレイヤーの無効中に値を送っていなかったことです。そのため次の試行の開始時に、前の試行の値が一瞬映っていました。現在は、レイヤーが無効のときも含めて値をすぐに送ります。`SaturationController` は保険として毎フレーム送り直していますが、値が同じなら SDK 側で何もしません。
- カメラのポストプロセスは有効にしないでください。パススルーに必要なアルファが失われます。
- 球にカーソルを合わせたときの変化は、拡大だけです。以前の発光表示は実機ビルドから落ちていて、被験者には見えていなかったため、削除しました。

## 最小利用例

ライブラリ部分だけを使う場合は、`OVRPassthroughLayer` を `SaturationController` に割り当てて、次のように呼びます。

```csharp
using QuestSaturation.Core; // ColorAdjustment

saturationController.SetSaturation(-0.8f);                       // 強い減彩度
saturationController.Apply(new ColorAdjustment(0.1f, 0f, -0.5f)); // 明るさ, コントラスト, 彩度
```

## License

MIT License. See LICENSE.

## Citation

If you use this software in academic work, please cite: Ryo Matsuzki, [software name], 2026.
