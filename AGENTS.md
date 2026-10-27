# 保持する外部検証環境

- `../mururu-blink-lipsync-tests/` は継続利用する非公開のUnity検証環境です。一時ファイル・研究アーカイブ・Git worktreeの残骸として削除しないでください。
- 製品と全テストの原本はこのリポジトリに置きます。外部環境の `Assets/Addon` を直接編集せず、`Documentation~/Run-Tests.ps1` で同期・実行してください。
- 構成・保持対象・必要な実データは `Documentation~/maintenance/TEST_ENVIRONMENT.md` を参照してください。
