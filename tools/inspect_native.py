"""Read-only native method evidence from the recovered metadata's RVA map."""
import argparse,bisect,hashlib,re,struct
from pathlib import Path
import capstone,pefile

PROJECT=Path(__file__).resolve().parents[1]
DEV=PROJECT.parent
def main():
    parser=argparse.ArgumentParser();parser.add_argument('match');args=parser.parse_args()
    methods={};current='';rva=None
    for line in (DEV/'1.0 Metadata/dump.cs').open(encoding='utf-8'):
        if '// TypeDefIndex:' in line:
            current=line.split('//')[0].strip()
        match=re.search(r'// RVA: 0x([0-9A-Fa-f]+)',line)
        if match:rva=int(match[1],16)
        elif rva is not None and '(' in line and not line.strip().startswith('['):
            methods.setdefault(rva,[]).append(current+' :: '+line.strip());rva=None
    addresses=sorted(methods);module=Path(r'E:\EscapeFromTarkov\GameAssembly.dll');pe=pefile.PE(str(module),fast_load=True)
    pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_EXCEPTION']])
    function_ends={entry.struct.BeginAddress:entry.struct.EndAddress for entry in getattr(pe,'DIRECTORY_ENTRY_EXCEPTION',[])}
    dis=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64);dis.detail=True
    output=['Module SHA256: '+hashlib.sha256(module.read_bytes()).hexdigest()]
    for address,names in methods.items():
        if not any(re.search(args.match,n) for n in names):continue
        index=bisect.bisect_right(addresses,address);end=addresses[index] if index<len(addresses) else address+4096
        end=min(end,function_ends.get(address,end))
        output+=['','\n'.join(names),f'RVA 0x{address:X}']
        for ins in dis.disasm(pe.get_data(address,min(end-address,8192)),pe.OPTIONAL_HEADER.ImageBase+address):
            if ins.mnemonic=='int3':break
            annotation=''
            if ins.mnemonic=='call' and ins.operands[0].type==capstone.x86.X86_OP_IMM:
                target=ins.operands[0].imm-pe.OPTIONAL_HEADER.ImageBase
                annotation=' ; '+' | '.join(methods.get(target,[]))
            if ins.mnemonic in ('movss','mulss','subss','addss'):
                for operand in ins.operands:
                    if operand.type==capstone.x86.X86_OP_MEM and operand.mem.base==capstone.x86.X86_REG_RIP:
                        loc=ins.address+ins.size+operand.mem.disp-pe.OPTIONAL_HEADER.ImageBase
                        annotation+=' ; f32='+str(struct.unpack('<f',pe.get_data(loc,4))[0])
            output.append(f'{ins.address:016X} {ins.mnemonic:8} {ins.op_str}{annotation}')
    folder=PROJECT/'Research/native';folder.mkdir(parents=True,exist_ok=True)
    path=folder/(re.sub('[^a-zA-Z0-9]+','-',args.match).strip('-')[:100]+'.txt')
    path.write_text('\n'.join(output),encoding='utf-8');print(path)
if __name__=='__main__':main()
