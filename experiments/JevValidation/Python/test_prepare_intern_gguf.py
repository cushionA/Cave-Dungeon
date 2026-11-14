import copy
import json
from pathlib import Path
import struct
import tempfile
from types import SimpleNamespace
import unittest

import numpy as np

from prepare_intern_gguf import (QWEN35_PRETOKENIZER, QWEN35_PRETOKENIZER_HASH, compile_cases,
                                 export_head, qwen35_recognition, tokenizer_check_text,
                                 validate_reference)


class InternGgufPreparationTests(unittest.TestCase):
    def test_RecognitionBackport_RequiresKnownFingerprintAndExactQwen35Configuration(self):
        config = {'pre_tokenizer': copy.deepcopy(QWEN35_PRETOKENIZER), 'normalizer': {'type': 'NFC'}}
        before = copy.deepcopy(config)
        record = qwen35_recognition(config, QWEN35_PRETOKENIZER_HASH)
        self.assertEqual('qwen35', record['gguf_pre'])
        self.assertFalse(record['pinned_converter_modified'])
        self.assertEqual(before, config)
        self.assertIsNone(qwen35_recognition(config, 'unknown-fingerprint'))
        config['pre_tokenizer']['pretokenizers'][0]['pattern']['Regex'] = 'different'
        with self.assertRaisesRegex(ValueError, 'exact Qwen3.5'):
            qwen35_recognition(config, QWEN35_PRETOKENIZER_HASH)
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory)
            (source / 'convert_hf_to_gguf.py').write_text('def check():\n    chktxt = "original probe"\n')
            self.assertEqual('original probe', tokenizer_check_text(source))

    def test_HeadRows_UseOriginalTiedBfloat16WeightsInSymbolOrder(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            values = np.arange(36, dtype=np.float32).reshape(12, 3) / 4
            words = (values.view(np.uint32) >> 16).astype('<u2')
            key = 'model.language_model.embed_tokens.weight'
            header = json.dumps({key: {'dtype': 'BF16', 'shape': [12, 3],
                                      'data_offsets': [0, words.nbytes]}}).encode()
            shard = root / 'language.safetensors'
            shard.write_bytes(struct.pack('<Q', len(header)) + header + words.tobytes())
            (root / 'model.safetensors.index.json').write_text(json.dumps({'weight_map': {key: shard.name}}))
            ids = [7, 2, 9, 0, 11, 4, 5, 1]
            head = export_head(root, {'tie_word_embeddings': True, 'text_config': {'hidden_size': 3,
                                'vocab_size': 12}}, ids, root / 'head.json')
            self.assertEqual(values[ids].tolist(), head['rows'])
            self.assertEqual(ids, head['symbol_token_ids'])
            self.assertEqual(3, head['hidden_size'])
            self.assertEqual(key, head['source_tensor'])
            self.assertEqual(values[ids].tolist(), json.loads((root / 'head.json').read_text())['rows'])
            with self.assertRaisesRegex(ValueError, 'tied'):
                export_head(root, {'text_config': {'hidden_size': 3, 'vocab_size': 12}}, ids,
                            root / 'invalid.json')

    def test_CompiledCases_PreserveOfficialTemplateAndCandidateOrder(self):
        case = {'case_id': 'rotated', 'question_id': 'strategy', 'request_sha256': 'original',
                'request': {'questions': {'strategy': {'criteria': dict(zip('hgfedcba', '12345678'))}}}}
        suite = {'cases': [case]}
        original = copy.deepcopy(suite)

        class Tokenizer:
            def apply_chat_template(self, messages, **options):
                self.messages = messages
                self.options = options
                return 'complete trained skeleton'

            def __call__(self, text, **options):
                self.tokenize_options = options
                return {'input_ids': [90, 91, 100, 92]}

            def encode(self, symbol, **options):
                return [ord(symbol)]

        tokenizer = Tokenizer()
        messages = [{'role': 'assistant', 'content': '<decision>'}]

        def compile_row(row, include_targets):
            self.assertFalse(include_targets)
            return SimpleNamespace(fields=('strategy',), symbols={'strategy': tuple('ABCDEFGH')}, messages=messages)

        cases = compile_cases(suite, tokenizer, compile_row,
                              lambda question: list(question['criteria'].items()), 100, 8)
        self.assertEqual(original, suite)
        self.assertEqual([90, 91, 100, 92], cases[0]['input_ids'])
        self.assertEqual(1, cases[0]['decision_position'])
        self.assertEqual(list('hgfedcba'), cases[0]['choice_ids'])
        self.assertEqual(list(range(65, 73)), cases[0]['symbol_token_ids'])
        self.assertEqual({'tokenize': False, 'add_generation_prompt': False, 'enable_thinking': False,
                          'add_vision_id': True}, tokenizer.options)
        self.assertEqual({'add_special_tokens': False}, tokenizer.tokenize_options)
        with self.assertRaisesRegex(ValueError, 'truncation'):
            compile_cases(suite, tokenizer, compile_row,
                          lambda question: list(question['criteria'].items()), 100, 3)

    def test_Reference_RejectsPartialOrDifferentRequestsAndOrdersBySymbols(self):
        suite = {'inputs_sha256': 'inputs', 'cases': [{'case_id': 'one', 'request_sha256': 'request'}]}
        cases = [{'case_id': 'one', 'request_sha256': 'request', 'input_ids': [1, 2, 3],
                  'choice_ids': list('hgfedcba')}]
        probabilities = dict(zip('abcdefgh', [1 / 8] * 8))
        reference = {'status': 'complete', 'backend': 'intern', 'model_id': 'id', 'revision': 'rev',
                     'inputs_sha256': 'inputs', 'results': [{'case_id': 'one', 'request_sha256': 'request',
                         'input_tokens': 3, 'candidate_ids': list('hgfedcba'), 'probabilities': probabilities,
                         'selected_id': 'a'}]}
        result = validate_reference(reference, suite, cases, 'id', 'rev')
        self.assertEqual([1 / 8] * 8, result[0]['reference_probabilities'])
        for changed in ({'status': 'running'}, {'inputs_sha256': 'other'}, {'results': []}):
            with self.subTest(changed=changed), self.assertRaises(ValueError):
                validate_reference({**reference, **changed}, suite, cases, 'id', 'rev')
        changed = copy.deepcopy(reference)
        changed['results'][0]['candidate_ids'].reverse()
        with self.assertRaises(ValueError):
            validate_reference(changed, suite, cases, 'id', 'rev')


if __name__ == '__main__':
    unittest.main()
