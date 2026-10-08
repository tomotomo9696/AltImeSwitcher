# AltImeSwitcher

[English](README.md) | [日本語](README.ja.md)

Win32フックを直接使用する、C# / .NET WinForms製のタスクトレイ常駐アプリケーションです。
AutoHotkeyやキーボードのリマップツールは不要です。ポータブル版には.NETランタイムを同梱しています。
C++製のバイナリではなく、マネージドコードによるWindowsデスクトップアプリケーションです。

本プロジェクトは、ソースコードの実装・リファクタリング、ドキュメント作成、アイコン画像生成に生成AIを使用して開発しています。
アプリケーションの実行時には生成AIを使用しません。

## リポジトリ構成

- `AltImeSwitcher/`: アプリケーションのソースコードとプロジェクトファイル。
- `AltImeSwitcher/Assets/`: 生成したPNG元画像と、アプリケーションに埋め込むICOファイル。
- `AltImeSwitcher.Tests/`: 回帰テストの実行プログラムと、Windows上での統合テスト。
- `tools/BuildIcon.ps1`: PNG元画像からICOファイルを再生成するスクリプト。

ビルド成果物 (`bin/`、`obj/`)、ローカルSDKの状態 (`.dotnet/`)、IDEのユーザー別設定、
ログ、テスト結果は `.gitignore` で除外しています。PNGとICOの両方をGitに登録してください。
ICOファイルはビルドに必要です。発行した実行ファイルはソースリポジトリにコミットせず、
GitHub Releasesの添付ファイルとして別途配布してください。

## 動作

| 入力 | 動作 |
| --- | --- |
| 左Alt単独を、設定した閾値より前に離す | 半角英数モード (IMEオフ) |
| 右Alt単独を、設定した閾値より前に離す | ひらがなモード (IMEオン) |
| いずれかのAltを、設定した閾値以上押し続ける | 通常のAltとして押下を送信し、離したときに解放を送信 |
| Altを押した直後に、別のキーやマウスボタン・ホイールを操作する | 閾値に達する前でも通常のAltの組み合わせ操作 |
| Ctrl、Shift、Windowsキー、または別のキーを押した状態でAltを押す | 通常の組み合わせ操作。IMEは切り替えない |
| 左右のAltを同時に押す | 両方とも通常のAltとして動作。IMEは切り替えない |

トレイアイコンを右クリックし、`長押し判定` から150、180、220 (既定)、300、400、500 msを選択できます。
選択中の項目にはチェックマークが付きます。変更はフックの再起動なしで、次にAltを押したときから適用されます。
すでに押しているAltについては、その押下時点の閾値を使用します。

設定は `%LOCALAPPDATA%\AltImeSwitcher\settings.json` にアトミックに保存し、次回起動時に復元します。
設定がない場合や内容が不正な場合は220 msを使用します。保存に失敗した場合も、今回の起動中は変更後の値を使用し、
警告通知を表示します。一時停止中の閾値変更にも対応しています。

生成した `Aあ` のキーキャップ画像を、実行ファイルとトレイアイコンの両方に埋め込んでいます。
ICOには16、20、24、32、40、48、64、128、256 pxの画像を格納しています。

アプリケーションはタスクトレイのみに常駐します。内部処理用のウィンドウは作成時から非表示とし、
タスクバーやAlt+Tabの一覧には表示しません。セッションや電源に関する通知は引き続き受信します。

右クリックメニューには `Pause` (一時停止) と `Exit` (終了) もあります。一時停止中は入力をそのまま通します。
一時停止、終了、セッションのロック・解除、スリープなどからの復帰時には保留中の状態をクリアし、
アプリケーションが正常に再送信したキーやボタンを解放します。
フック開始時点ですでに押されているキーは、離されるまでそのまま通します。

変換対象は物理的なキーボード・マウス入力のみです。他のアプリケーションが注入した入力イベントはそのまま通します。
Ctrl+右Alt (AltGrを含む) の動作も維持します。
IME切り替え要求は、実行前に対象の前面ウィンドウが変わった場合には破棄します。

## 起動

リポジトリのルートから見た、発行後の実行ファイルの場所は次のとおりです。

```text
AltImeSwitcher\bin\Release\net9.0-windows\win-x64\publish\AltImeSwitcher.exe
```

更新版を起動する前に、旧版を終了してください。トレイアイコンは、通知領域の隠れているアイコンの一覧に入る場合があります。
サインイン時に自動起動するには、発行した実行ファイルへのショートカットを `shell:startup` に配置してください。

ログの場所: `%LOCALAPPDATA%\AltImeSwitcher\AltImeSwitcher.log` (1 MiBでローテーション)。
ログには診断情報を記録します。入力した文章やキー操作の履歴は記録しません。

## ビルド

.NET 9 SDKとWindows Desktopターゲティングパックを用意し、リポジトリのルートで実行してください。

```powershell
dotnet build .\AltImeSwitcher.Tests\AltImeSwitcher.Tests.csproj -c Release
dotnet publish .\AltImeSwitcher\AltImeSwitcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

.NET 9 Windows Desktop Runtimeがインストール済みの環境向けには、`--self-contained false` に変更すると
配布サイズを小さくできます。その場合も、発行先ディレクトリ内のファイルをまとめて配布してください。

## 検証

回帰テストの実行プログラムは、外部のテストパッケージに依存しません。

```powershell
.\AltImeSwitcher.Tests\bin\Release\net9.0-windows\AltImeSwitcher.Tests.exe
.\AltImeSwitcher.Tests\bin\Release\net9.0-windows\AltImeSwitcher.Tests.exe --hook-lifecycle --tray-lifecycle
.\AltImeSwitcher.Tests\bin\Release\net9.0-windows\AltImeSwitcher.Tests.exe --integration
```

1つ目のコマンドでは、ABIレイアウトとキーの状態遷移を検証します。短押し、閾値の境界、キーリピート、
左右Altの同時操作、入力再送信の順序、マウスとの組み合わせ、失敗、一部のみの送信、フォーカス変更、終了処理が対象です。
すべての閾値、押下中の設定変更、設定の保存・復元と異常時の復旧、埋め込みアイコンの各サイズ、
Windowsによる実行ファイルからのアイコン抽出も確認します。

2つ目のコマンドでは、実際のキーボード・マウスフックの登録と解除を20回繰り返し、トレイ常駐処理の開始・終了も確認します。
テスト専用の設定を使用し、実際の閾値メニュー、設定ファイル、一時停止・再開を検証します。ユーザーの設定は変更しません。
起動時、一時停止からの再開後、メッセージループ実行中のウィンドウの非表示状態、タスクバー除外のスタイル、
非表示ウィンドウを経由したコールバックの実行も確認します。このテストではキーボード入力を生成しません。

統合テストのコマンドは検証用ウィンドウを開き、専用の識別タグを付けたテスト入力を、実際のフックと状態制御処理に通します。
左右Alt+F、素早い組み合わせ操作、短押し、終了時について、OS上のAltの状態と `WM_SYSKEYDOWN` の受信を確認します。
入力は検証用ウィンドウが前面にある間だけ送信します。Windowsが前面への切り替えを許可しない場合は、入力送信前にテストを失敗させます。
このテストはフックと入力再送信の動作を確認するものであり、実際のIMEは変更せず、IMEとの互換性を保証するものではありません。

メモ帳での手動確認項目: 左右Altの短押し、Altを長押ししてからFを押す操作、素早いAlt+F、長押しでのAlt+Tab、
Ctrl+Altの組み合わせ、一時停止・再開、Altを押した状態での終了。
IMEモードの確認前には、半角英数・ひらがな・カタカナを切り替えて試してください。

## 設計

- `AltKeyController`: 入力送信処理を差し替え可能な、決定的な状態機械。
- `AltKeyHook`: キーボード・マウスの両フックと長押し判定タイマーを処理する、専用STAスレッドのメッセージループ。
- `InputSender` / `NativeMethods`: ABI検証、識別タグ付き入力、順序を保証する一括送信、戻り値の確認、ポインターサイズに対応したWin32宣言。
- `ImeController`: フォーカスのあるコントロールを特定し、フック処理とは別のUIスレッドからタイムアウト付きIMMメッセージを送信。ローマ字入力・かな入力の設定は維持。
- `TrayApplicationContext`: 一時停止・再開、世代番号によるIME要求の有効性確認、明示的な解放処理。入力再送信に失敗した場合はフックを一時停止し、通知を表示。
- `AppSettings`: 選択式の閾値、設定値の検証、アトミックな保存。
- `ApplicationIcon`: 外部ファイルに依存せず、埋め込まれた生成アイコンを読み込む処理。
- `HiddenDispatcher`: UI処理の呼び出しと電源・セッション通知に使用する、常に非表示のトップレベルウィンドウ。タスクバーには表示せず、アクティブ化もしない。

再送信するキーやボタンの押下・解放は両方ともキューに入れ、直後に届いた物理的な解放イベントが、再送信する押下を追い越さないようにしています。
状態制御処理は正常に送信できた入力だけを記録し、再送信に失敗した場合は元のイベントをそのまま通します。

## 制限事項

`SendInput` はUIPIの制約を受けます。管理者権限のないプロセスから、管理者権限で動く対象へ入力を注入することはできません。
必要に応じて、対象と同じ権限レベルで実行してください。UACのセキュアデスクトップやサインイン画面は本アプリケーションの対象外です。
強制終了時には通常の終了処理を実行できないため、トレイメニューの `Exit` を使用してください。

IMEの変換モードの動作は、Microsoft IME、他社製IME、TSF専用コントロールなどによって異なります。
`VK_IME_ON/OFF` とフォーカス先へのIMMメッセージを併用していますが、入力の注入に成功しただけでは、
対象が実際にモードを変更したことまでは確認できません。
Raw Inputを使用するアプリケーションや、注入された入力を拒否するアプリケーションでは、通常のデスクトップアプリケーションと動作が異なる場合があります。
長押しの閾値は目安です。Windowsのタイマースケジューリングで処理が遅れる場合があるため、キーを離した時点でも経過時間を確認しています。

APIリファレンス:
[SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)、
[LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)、
[SendMessageTimeout](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw)、
[ImmSetConversionStatusの制限](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immsetconversionstatus)。

## アイコンの元画像

生成した元画像は `AltImeSwitcher\Assets\AltImeSwitcher.png` です。
ICOを再生成するには、Windows PowerShellまたはWindows上のPowerShellで、リポジトリのルートから次を実行してください。

```powershell
.\tools\BuildIcon.ps1 -Source .\AltImeSwitcher\Assets\AltImeSwitcher.png -Destination .\AltImeSwitcher\Assets\AltImeSwitcher.ico
```
