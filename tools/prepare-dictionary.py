import csv, gzip, json, pathlib
root = pathlib.Path(__file__).resolve().parent.parent
count = 0
with (root/'tools/ecdict.csv').open(encoding='utf-8-sig', newline='') as src, gzip.open(root/'vendor/dictionary/ecdict.tsv.gz', 'wt', encoding='utf-8', newline='\n') as dst:
    for row in csv.DictReader(src):
        word = row['word'].strip().lower()
        if not word or not row.get('translation'): continue
        fields = [word, row.get('phonetic',''), row.get('translation',''), row.get('definition',''), row.get('exchange','')]
        fields = [s.replace('\t',' ').replace('\r','').replace('\n',r'\n') for s in fields]
        dst.write('\t'.join(fields)+'\n')
        count += 1
(root/'vendor/dictionary/info.json').write_text(json.dumps({'entries':count,'source':'https://github.com/skywind3000/ECDICT','license':'MIT'},ensure_ascii=False,indent=2),encoding='utf-8')
print(f'Offline dictionary entries: {count}')
