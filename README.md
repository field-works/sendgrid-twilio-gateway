# SendGrid to Twilio Gateway

EメールとFAXの送受信を中継するGatewayシステム（以後本システムと呼ぶ）です。

以下のことが実現できます。

- Eメール経由でFAXを送信  
  PDF形式の添付画像をイメージ化してFAXで送信します。
- 受信したFAXをEメールで転送  
  FAXイメージは，PDF形式で添付されます。

本システムはASP.NET Coreで実装されていて，Azure App Serviceで運用することを想定しています。

Eメールの送受信にはSendGridを，FAXの送受信にはTwilioを利用しています。

## 前提条件

- Eメールの配信に利用できる（サブ）ドメイン名を取得していること。
- SendGridのアカウントを取得済みであること。
- Twilioのアカウントを取得済みであること。
- Microsoft Azureのアカウントを取得済みであること。

## [SendGrid] Eメール配信設定
### API Keyの取得

    Settings > API Keys > Create API Key

メール送信が可能な権限を持ったAPIキーを作成してください。

### 独自ドメイン設定

    Settings > Sender Authentication > Domain Authentication

独自ドメインを利用するための設定を行ってください。

次に，Domain Authentication設定画面の指示にしたがって，DNSにレコードを追加してください。

## [Twilio] FAX配信設定
### FAX送受信用番号の取得

    電話番号 > 番号を購入

FAX配信用の電話番号を購入してください。

### APIキーの取得

    Runtime > APIキー > 新規APIキー

FAX送信用のAPIキーを作成します。

## [Azure] 本システムのインストール
### App Serviceの作成とアプリケーションのデプロイ

    ホーム > App Service > ＋追加

App Serviceを作成し，本アプリケーションをデプロイします。

### ストレージアカウントの作成

    ホーム > ストレージアカウント > ＋追加

FAX送信時にPDFファイルを一時保存するためのストレージアカウントを作成します。

### アプリケーション設定

    ホーム > App Service > アプリケーション設定

サイト固有の設定を追加します。

    Settings:SendGrid:ApiKey（必須） - SendGridで作成したAPIキー

    Settings:Twilio:Number（必須） - Twilioで購入した電話番番号（E.164形式）
    Settings:Twilio:UserName（必須） - Twilioで作成したAPIキーのSID
    Settings:Twilio:Password（必須） - Twilioで作成したAPIキーのSecret

    Settings:Azure:StorageConnectionString（必須） - ストレージアカウントへの接続文字列
    Settings:Azure:ContainerSid（既定値：outgoing） - 一時ファイル格納用コンテナー名

    Settings:Station:CountryCode（既定値：81） - FAX送信先番号書き換え時に使用する国番号
    Settings:Station:DomainName（必須） - メール配信用独自ドメイン名
    Settings:Station:AgentAddr（必須） - Eメール送信時の送信元アドレス
    Settings:Station:InboxAddr（必須） - FAXイメージ配信先Eメールアドレス
    Settings:Station:Quality（既定値：Fine） - FAX送信時のデフォルト画質（Standard, Fine, Superfine）

### ログ出力設定

    ホーム > App Service > 診断ログ

必要に応じて，診断ログの設定を行います。

## [SendGrid] Eメール受信設定

    Settings > Inbound Parse > Add Host & URL

Inbound Parseの設定を行います。

    Receiving Domain：取得した独自ドメイン
    Destination URL：https://<App Service名>.azurewebsites.net/api/outgoing

## [Twilio] FAX受信設定

    電話番号 → 番号の管理 → <電話番号をクリック>

購入した電話番号を選択し，Webhook設定を追加します。

    受け付ける着信：Faxes
    構成内容：Webhooks, TwiML Bins, Functions, Studio, or Proxy
    FAX受信時：
        Webhook
        https://<App Service名>.azurewebsites.net/api/incoming
        HTTP POST

## 利用方法
### FAXの送信

下記宛先に，PDFファイルを一つ添付したEメールを送信します。

    To: <FAX送信先電話番号>@<独自ドメイン名>

- FAX送信先電話番号の書式は以下のとおりです。
  - E.164形式の国際番号（+<国番号><国内電話番号>）または0始まりの国内番号
  - 0始まりの番号を指定した場合は，`Settings:Station:CountryCode`で設定した国番号を使用して，
    内部的にE.164形式に変換されます。
  - 区切り文字（ハイフンや空白文字など）は使用できません。
- FAX送信結果は，Eメール送信アドレスに返信されます。
- FAX送信に成功した場合は，`Settings:Station:InboxAddr`で設定したアドレスにCcされます。

### FAXの受信

FAX配信用電話番号に着信すると，受信結果を下記の宛先に送信します。

    To: <FAXイメージ配信先Eメールアドレス>

- FAXイメージ配信先Eメールアドレスは，`Settings:Station:InboxAddr`で設定したアドレスとなります。
- Eメール送信者は，`<FAX送信元電話番号>@<独自ドメイン名>`に設定されます。

## ライセンス

このソフトウェアは，MITライセンスのもとで公開されています。
LICENSE.txtを参照してください。
