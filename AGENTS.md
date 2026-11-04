# 開発・検証ルール

- 継続利用する非公開のUnity検証環境は製品リポジトリの外に保持します。一時ファイル・研究アーカイブ・Git worktreeの残骸として削除したり、製品リポジトリへ移動・コピーしたりしないでください。
- 製品と全テストの原本はこのリポジトリの `Editor/`・`Runtime/`・`Tests/` に置きます。外部環境の `Assets/Addon` を直接編集せず、`Documentation~/Run-Tests.ps1` で同期・実行してください。
- スクリプトの既定の検証環境は `../mururu-blink-lipsync-tests/` です。配置が異なる場合は `-ProjectPath` で実際の検証環境を指定してください。
- 構成・保持対象・必要な実データ・実行方法は [専用Unity検証環境の保守手順](Documentation~/maintenance/TEST_ENVIRONMENT.md) を参照してください。
