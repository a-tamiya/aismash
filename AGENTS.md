# AGENTS.md

このファイルはOpenAI CodexがこのリポジトリでAgentとして作業する際の指針を定めます。

## プロジェクト概要

**AI Prompt Fighter** — プレイヤーがテキストプロンプトを入力し、AIがキャラクター・技を生成する2D横視点対戦アクションゲーム。Unityで開発。

GitHubリポジトリ: https://github.com/a-tamiya/aismash

## 開発方針

- このプロジェクトは **OpenAI Codex** と **Claude Code** の両方で並行して開発する
- 要件の変更・追加が生じた場合は、**作業と同時に `requirements.md` を更新**する
- 変更を加えた場合は、**作業完了時に Gitコミットを作成し、`origin/main` へ push する**

## 作業完了時のフロー

1. 変更内容を確認 (`git status`, `git diff`)
2. 関連ファイルをステージング (`git add <files>`)
3. コミットメッセージを作成して commit
4. `git push origin main` で push

## コミットメッセージ規約

- **日本語で簡潔に**書く

## 要件管理

- 要件の追加・変更・削除は `requirements.md` に反映する
- `requirements.md` の変更は通常の変更と同じコミットに含めてよい

## リポジトリ構成

```
aismash/
├── CLAUDE.md           # Claude Code向け指針
├── AGENTS.md           # Codex向け指針（本ファイル）
├── requirements.md     # 要件定義書
└── aismash_unity/      # Unityプロジェクト
```
