import sys
import clr
# Append to path in user code, not here
#sys.path.append('../LibSLH/bin/Debug/netstandard2.0')
clr.AddReference('LibreMetaverse')
clr.AddReference('LibSLH')
import asyncio

# Commonly used
from OpenMetaverse import *
from LibSLH import *

import json
from types import SimpleNamespace


class Client(SLHClient):
    PRODUCT = 'SLHBot'
    VERSION = '1.0.0'

    def __init__(self):
        super().__init__()
        self._update_coro = None
        self._event_loop = None

    async def update(self):
        await asyncio.sleep(1)  # Monkey-patch ready

    async def _call_update(self):
        while self.Network.Connected:
            await self.update()

    def _handle_login_progress(self, sender, args):
        if args.Status == LoginStatus.Success:
            self._on_login_success()

    def _on_login_success(self):
        self._update_coro = asyncio.run_coroutine_threadsafe(
            self._call_update(), self._event_loop)

    def _handle_logged_out(self, sender, args):
        self._update_coro.cancel()

    @classmethod
    def load_config(cls, path='config.json'):
        with open(path, 'r') as config_fp:
            return SimpleNamespace(**json.load(config_fp))

    def start(self, config=None):
        if not config:
            config = self.load_config()
        self._event_loop = asyncio.get_event_loop()
        login_params = self.Network.DefaultLoginParams(
            config.FirstName, config.LastName, config.Password, self.PRODUCT,
            self.VERSION)
        self.Network.BeginLogin(login_params)
        self.Network.LoginProgress += self._handle_login_progress
        self.Network.LoggedOut += self._handle_logged_out

    def stop(self):
        self.Network.Logout()
