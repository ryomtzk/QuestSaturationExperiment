QuestSaturationExperiment
Full-Field Color Manipulation Library for Meta Quest 3

Author: Ryo Matsuzaki
License: MIT

Meta Quest 3 のパススルー映像全体の彩度をリアルタイムに操作するための Unity プロジェクトです。OVRPassthroughLayer.SetBrightnessContrastSaturation を毎フレーム適用することで、Building Blocks 由来の上書きを抑えつつ、視野全体の彩度を [-1, +1] の範囲で任意に変化させられます。

サンプルとして、3条件（無変化 / 強い減彩度 / 強い過彩度）の被験者選択タスクを同梱しています。

Requirements
Unity 6000.3.7f1 / URP 17.3 / Meta XR SDK 201.0.0 / OpenXR 1.16 / Meta Quest 3 (Android build)

Scripts
Assets/Scripts/ に以下のスクリプトがあります。

SaturationController.cs — OVRPassthroughLayer に brightness / contrast / saturation を毎フレーム適用する本体。SetSaturation(float) を外から呼ぶだけで使えます。
SaturationCurve.cs — 条件ごとの目標値と時間プロファイル（3秒ホールド → 10秒リニアランプ → 保持）。
ConditionSelector.cs — 右コントローラのレイで3つの球を選ぶ UI。
SphereOption.cs — 選択肢の球（ホバー時に拡大＋エミッション）。
ExperimentManager.cs — Selection / Experiment の2フェーズを管理。右Aボタン2秒長押しで Selection に戻ります。
条件と彩度ターゲット:

A: 0.0
B: -0.8
C: +0.8
最小利用例
ライブラリ部分だけ使いたい場合は、OVRCameraRig の OVRPassthroughLayer を SaturationController に割り当てて、任意のタイミングで以下を呼びます。

saturationController.SetSaturation(-0.8f); // 強い減彩度
Setup
Unity 6000.3.7f1 でプロジェクトを開き、パッケージ解決を待ちます。
Assets/Scenes/PassthroughScene.unity を開きます。
Build target を Android に切り替え、Meta Quest 3 を USB 接続して Build & Run します。
エディタ上ではパススルーを再生できないため、動作確認は実機で行ってください。

Controls
右コントローラで照準、人差し指トリガで決定、Aボタン2秒長押しで選択画面に戻ります。

License
MIT License. See LICENSE.

Citation
If you use this software in academic work, please cite: Ryo Matsuzki, [software name], 2026.
