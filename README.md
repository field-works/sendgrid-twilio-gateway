# SendGrid to Twilio Gateway

Eメールを介してFAXの送受信を行うためのアプリケーションです。

ASP.NET Coreで実装されています。
Azure App Serviceで運用することを想定しています。

Eメールの送受信にはSendGridを，FAXの送受信にはTwilioを利用します。

### 前提条件

− SendGridのアカウントを登録済みであること。
- Twilioのアカウントを登録済みであること。
- Microsoft Azureのアカウントを登録済みであること。
- ドメイン名を取得していること。

## Eメール送受信設定
### [SendGrid] API Keyの取得

メール送信用のAPIキーを作成します。

### [SendGrid] Domain Whitelabel設定

### [DNS] MXレコードとCNAMEの追加

## FAX送受信設定
### [Twilio] FAX送受信用番号の取得

### [Twilio] APIキーの取得

## アプリケーション設定
### [Azure] App Serviceのデプロイ

App Serviceを作成し，アプリケーションをデプロイする。

### [Azure] ストレージアカウントの作成


### [Azure] アプリケーション設定

    Settings:SendGrid:ApiKey（必須） - 
    
    Settings:Twilio:Number（必須） - Twilioで購入した電話番番号（E.164形式）
    Settings:Twilio:UserName（必須） - APIキーのSID
    Settings:Twilio:Password（必須） - APIキーのSecret
    
    Settings:Azure:StorageConnectionString（必須） - 
    Settings:Azure:ContainerSid（既定値：outgoing） - 
    
    Settings:Station:CountryCode（既定値：81） - FAX送信先番号が`0`始まりの場合に，この国コードを使用してE.164形式の番号に変換します。
    Settings:Station:DomainName - MXレコードに設定したドメイン名
    Settings:Station:AgentAddr（必須） - Eメール送信時の送信元アドレス
    Settings:Station:InboxAddr（必須） - 受信したFAXイメージ，FAX送信結果の送信先アドレス
    Settings:Station:Quality（既定値：Fine） - FAX送信時のデフォルト画質（Standard, Fine, Superfine）

### [Azure] ログ出力設定

### [SendGrid] Inbound Parse設定

    https://<App Service名>.azurewebsites.net/api/outgoing

### [Twilio] Webhook設定

    https://<App Service名>.azurewebsites.net/api/incoming

## 使用方法
### FAX受信

    From: <FAX送信元電話番号>@{Settings:Station:DomainName}
    To: {Settings:Station:InboxAddr}

### FAX送信

    From: <メール送信元アドレス>
    To: <FAX送信先電話番号>@{Settings:Station:DomainName}

成功した場合：

    From: {Settings:Station:AgentAddr}
    To: <メール送信元アドレス>
    Cc: {Settings:Station:InboxAddr}

失敗した場合：

    From: {Settings:Station:AgentAddr}
    To: <メール送信元アドレス>
