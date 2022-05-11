# Authorize.Net payment integration module

Authorize.Net Accept.js payment module provides integration with Authorize.Net using <a href="http://developer.authorize.net/api" target="_blank">Accept.js</a> and <a href="https://developer.authorize.net/api/reference/index.html#accept-suite-create-an-accept-payment-transaction">Authorize.net API</a>.

# Store settings UI

![Store settings](docs/media/authorize-net-store-settings.png)

# Settings

The module can be configured in the following places:
- Platform config file: appsettings.json
- Store-specific settings: Stores -> (your store) -> Payment methods -> Authorize.Net -> Settings

Confidential Authorize.Net account settings should be configured in appsetting.json:
* **API login id** - Authorize.Net API login ID from credentials
* **Transaction key** - Authorize.Net transaction key from credentials

```json
"Payments": {
    "AuthorizeNet": {
        "ApiLogin": "Your api login", 
        "TxnKey": "Your transaction key",
    }
}
```

Nonconfidential settings should be configured at Store-specific settings - Stores -> (your store) -> Payment methods -> Authorize.Net -> Settings:
* **Mode** - Authorize.Net payment gateway mode: test or real.
* **Process Payment action URL** - URL for post process payment in VC Storefront: {put storefront url here}/api/payments/an/registerpayment.
* **Payment action type** - Action type of payment: Sale or Authorize/Capture. In "Sale" mode a transaction is automatically submitted for settlement. In "Authorize/Capture" the transaction amount is sent for authorization only. The merchant must manually capture the transaction in the Merchant Interface.


## License

Copyright (c) Virto Solutions LTD.  All rights reserved.

Licensed under the Virto Commerce Open Software License (the "License"); you
may not use this file except in compliance with the License. You may
obtain a copy of the License at

<http://virtocommerce.com/opensourcelicense>

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
implied.
