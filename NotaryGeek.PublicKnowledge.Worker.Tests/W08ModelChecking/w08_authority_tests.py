"""Primitive self-checks, separate from the production SDK schedule invariants."""

import base64
from concurrent.futures import ThreadPoolExecutor
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from urllib.parse import quote, urlencode
import xml.etree.ElementTree as ET

from w08_authority import AMBIENT_IDENTITY_VARIABLES, Authority


def request(method, name="object", body=b"", headers=None, query=None, host="storage.invalid", request_id=1):
    merged = {"x-ms-blob-type": "BlockBlob"} if method == "PUT" and name else {}
    merged.update(headers or {})
    return {"id": request_id, "kind": "http", "method": method,
            "url": f"https://{host}/fixture" + ("/" + quote(name, safe="/") if name else "")
                   + ("?" + urlencode(query) if query else ""),
            "headers": merged, "body": base64.b64encode(body).decode("ascii")}


class AuthorityTests(unittest.TestCase):
    def setUp(self):
        self.environment = patch.dict(os.environ, {key: "" for key in AMBIENT_IDENTITY_VARIABLES})
        self.environment.start()
        self.addCleanup(self.environment.stop)
        directory = tempfile.TemporaryDirectory(prefix="w08-authority-")
        self.addCleanup(directory.cleanup)
        self.path = Path(directory.name) / "authority.sqlite3"
        self.authority = Authority(self.path)

    def test_exact_bytes_etags_and_independent_objects_survive_reopening(self):
        data = b'\x00\xff{"opaque":"bytes"}\r\n'
        first = self.authority.seed("a", data)
        second = self.authority.seed("b", b"unrelated")
        reopened = Authority(self.path)
        self.assertEqual(first, reopened.get("a"))
        self.assertEqual({"a": first, "b": second}, reopened.snapshot())
        response = reopened.handle(request("GET", "a", headers={"Range": "bytes=0-3"}))
        self.assertEqual(data, base64.b64decode(response["body"]))
        self.assertEqual(first.etag, response["headers"]["ETag"])
        updated = reopened.seed("a", b"replacement")
        self.assertNotEqual(first.etag, updated.etag)
        self.assertEqual(second, self.authority.get("b"))
        head = reopened.handle(request("HEAD", "a"))
        self.assertEqual("", head["body"])
        self.assertEqual(str(len(b"replacement")), head["headers"]["Content-Length"])

    def test_stale_and_missing_etags_reject_without_mutating_bytes_or_version(self):
        first = self.authority.handle(request("PUT", body=b"first", headers={"If-None-Match": "*"}))
        etag = first["headers"]["ETag"]
        updated = self.authority.handle(request("PUT", body=b"second", headers={"If-Match": etag}))
        for operation in (
            request("PUT", body=b"third", headers={"If-Match": etag}),
            request("PUT", body=b"third", headers={"If-None-Match": "*"}),
            request("PUT", "missing", b"third", headers={"If-Match": etag}),
            request("GET", headers={"If-Match": etag}),
            request("HEAD", headers={"If-Match": etag}),
        ):
            self.assertEqual(412, self.authority.handle(operation)["status"])
        self.assertEqual(b"second", self.authority.get("object").body)
        self.assertEqual(updated["headers"]["ETag"], self.authority.get("object").etag)
        self.assertIsNone(self.authority.get("missing"))
        self.assertEqual(404, self.authority.handle(request("GET", "missing"))["status"])
        # Failed condition checks do not allocate versions.
        self.assertEqual('"3"', self.authority.seed("next", b"x").etag)

    def test_conditional_creation_is_atomic_across_authority_instances(self):
        def put(index):
            return Authority(self.path).handle(request(
                "PUT", "contended", str(index).encode(), {"If-None-Match": "*"}))["status"]
        with ThreadPoolExecutor(max_workers=8) as pool:
            statuses = list(pool.map(put, range(8)))
        self.assertEqual(1, statuses.count(201))
        self.assertEqual(7, statuses.count(412))

    def test_compare_exchange_has_one_winner_across_independent_instances(self):
        initial = self.authority.seed("contended", b"initial")
        def put(index):
            return Authority(self.path).handle(request(
                "PUT", "contended", str(index).encode(), {"If-Match": initial.etag}))["status"]
        with ThreadPoolExecutor(max_workers=8) as pool:
            statuses = list(pool.map(put, range(8)))
        self.assertEqual(1, statuses.count(201))
        self.assertEqual(7, statuses.count(412))

    def test_container_creation_and_paginated_xml_listing_persist(self):
        create = request("PUT", "", query={"restype": "container"})
        self.assertEqual(201, self.authority.handle(create)["status"])
        reopened = Authority(self.path, page_size=2)
        self.assertEqual(409, reopened.handle(create)["status"])
        names = ["prefix/z", "prefix/a&<.json", "prefix/c", "prefix/b", "other/ignored"]
        saved = {name: reopened.seed(name, name.encode()) for name in names}
        listed, marker = [], ""
        for _ in range(4):
            response = reopened.handle(request("GET", "", query={
                "restype": "container", "comp": "list", "prefix": "prefix/", "marker": marker,
            }))
            root = ET.fromstring(base64.b64decode(response["body"]))
            page = root.findall("./Blobs/Blob")
            self.assertLessEqual(len(page), 2)
            for blob in page:
                name = blob.findtext("Name")
                listed.append(name)
                self.assertEqual(saved[name].etag, blob.findtext("Properties/Etag"))
                self.assertEqual(str(len(saved[name].body)), blob.findtext("Properties/Content-Length"))
            marker = root.findtext("NextMarker")
            if not marker:
                break
            reopened = Authority(self.path, page_size=2)
        self.assertEqual(sorted(name for name in names if name.startswith("prefix/")), listed)
        self.assertFalse(marker)

    def test_queue_base64_xml_and_effect_observations_preserve_duplicates(self):
        create = request("PUT", "", host="queue.invalid")
        self.assertEqual(201, self.authority.handle(create)["status"])
        self.assertEqual(204, Authority(self.path).handle(create)["status"])
        body = b'\x00{"message":"public fixture"}\xff'
        xml = b"<QueueMessage><MessageText>" + base64.b64encode(body) + b"</MessageText></QueueMessage>"
        for _ in range(2):
            response = self.authority.handle(request("POST", "messages", xml, host="queue.invalid"))
            self.assertEqual(201, response["status"])
            self.assertTrue(ET.fromstring(base64.b64decode(response["body"])).findtext("QueueMessage/MessageId"))
        identity = {"opaque": ["same", 1]}
        self.assertEqual(1, self.authority.effect(identity))
        self.assertEqual(2, self.authority.effect(identity))
        reopened = Authority(self.path)
        self.assertEqual([body, body], reopened.queue_bodies())
        self.assertEqual([identity, identity], reopened.effects())

    def test_commit_survives_process_kill_without_a_delivered_operation_reply(self):
        script = """
import json, sys
from w08_authority import Authority
authority = Authority(sys.argv[1])
reply = authority.handle(json.loads(sys.argv[2]))
# This is a test rendezvous, not the HTTP reply; reply is never serialized.
print('committed', flush=True)
sys.stdin.readline()
raise AssertionError('the parent must kill this process before replying')
"""
        import json
        child = subprocess.Popen(
            [sys.executable, "-c", script, str(self.path), json.dumps(request(
                "PUT", "no-reply", b"durable", {"If-None-Match": "*"}))],
            cwd=Path(__file__).resolve().parent, stdin=subprocess.PIPE,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
        )
        try:
            # communicate(timeout) cannot be used for a child intentionally held
            # at this boundary. A thread makes the pipe rendezvous bounded.
            with ThreadPoolExecutor(max_workers=1) as pool:
                line = pool.submit(child.stdout.readline)
                try:
                    self.assertEqual("committed\n", line.result(timeout=10))
                finally:
                    child.kill()
            child.wait(timeout=10)
            reopened = Authority(self.path)
            saved = reopened.get("no-reply")
            self.assertEqual(b"durable", saved.body)
            self.assertEqual('"1"', saved.etag)
            self.assertEqual(412, reopened.handle(request(
                "PUT", "no-reply", b"duplicate", {"If-None-Match": "*"}))["status"])
            self.assertEqual(saved, reopened.get("no-reply"))
        finally:
            if child.poll() is None:
                child.kill()
                child.wait(timeout=10)
            child.stdin.close()
            child.stdout.close()
            child.stderr.close()

    def test_broken_cas_negative_control_is_detected(self):
        def require_one_create(authority):
            statuses = [authority.handle(request("PUT", "negative", bytes([i]),
                                                 {"If-None-Match": "*"}))["status"] for i in range(2)]
            self.assertEqual([201, 412], statuses, "conditional creation admitted two winners")
        require_one_create(self.authority)
        broken = Authority(self.path.parent / "broken.sqlite3", broken_cas=True)
        with self.assertRaisesRegex(AssertionError, "two winners"):
            require_one_create(broken)
        old = broken.get("negative")
        broken.seed("negative", b"newer")
        stale = broken.handle(request("PUT", "negative", b"stale", {"If-Match": old.etag}))
        with self.assertRaises(AssertionError):
            self.assertEqual(412, stale["status"], "stale compare-exchange must fail")

    def test_invalid_requests_and_credentials_cannot_change_durable_state(self):
        bad_requests = []
        for url in (
            "http://storage.invalid/fixture/object", "https://example.com/fixture/object",
            "https://user:password@storage.invalid/fixture/object", "https://storage.invalid:443/fixture/object",
            "https://storage.invalid/elsewhere/object", "https://storage.invalid/fixture/object#fragment",
            "https://storage.invalid/fixture/object?sig=fixture", "https://storage.invalid/fixture/object?SV=fixture",
        ):
            bad_requests.append({**request("PUT", body=b"bad"), "url": url})
        for header in ("Authorization", "authorization", "Proxy-Authorization", "Cookie"):
            bad_requests.append(request("PUT", body=b"bad", headers={header: "synthetic"}))
        bad_requests.append({**request("PUT"), "body": "not base64"})
        bad_requests.append({**request("PUT"), "kind": "provider"})
        for operation in bad_requests:
            with self.subTest(operation=operation), self.assertRaises(ValueError):
                self.authority.handle(operation)
        self.assertEqual({}, self.authority.snapshot())
        with patch.dict(os.environ, {"AZURE_CLIENT_ID": "synthetic"}), self.assertRaises(ValueError):
            Authority(self.path)


if __name__ == "__main__":
    unittest.main()
