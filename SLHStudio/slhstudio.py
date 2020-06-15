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
        self._coroutines = dict()
        self._event_loop = asyncio.get_event_loop()
        self._coroutines_ready = False

    async def _coroutine_wrapper(self, method):
        coro_method = method(self)
        while True:
            #print(f"self._coroutines_ready == {self._coroutines_ready}")
            if self._coroutines_ready:
                try:
                    await next(coro_method)
                except asyncio.CancelledError as cancelled:
                    raise cancelled
                except Exception as e:
                    print(e)
            else:
                await asyncio.sleep(1);

    def _handle_login_progress(self, sender, args):
        if args.Status == LoginStatus.Success:
            self._on_login_success()

    def _on_login_success(self):
        self._coroutines_ready = True
        pass

    def _handle_logged_out(self, sender, args):
        self._coroutines_ready = False
        pass

    def start_coroutine(self, method, key=None):
        if not key:
            key = id(method)
        if key in self._coroutines:
            self.stop_coroutine(key)
        coroutine = asyncio.run_coroutine_threadsafe(
            self._coroutine_wrapper(method), self._event_loop)
        self._coroutines[key] = coroutine

    def stop_coroutine(self, key):
        self._coroutines[key].cancel()
        del self._coroutines[key]

    @classmethod
    def load_config(cls, path='config.json'):
        with open(path, 'r') as config_fp:
            return SimpleNamespace(**json.load(config_fp))

    def start(self, config=None):
        if not config:
            config = self.load_config()
        login_params = self.Network.DefaultLoginParams(
            config.FirstName, config.LastName, config.Password, self.PRODUCT,
            self.VERSION)
        self.Network.BeginLogin(login_params)
        self.Network.LoginProgress += self._handle_login_progress
        self.Network.LoggedOut += self._handle_logged_out

    def stop(self):
        self.Network.Logout()
