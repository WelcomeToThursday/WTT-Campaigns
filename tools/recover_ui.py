"""Recover only seasonal UI hierarchies using the supplied IL2CPP metadata.

Never loads game code. Output remains in CJ-SDK; icons are served separately.
"""
import hashlib,json,struct
from pathlib import Path
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.TypeTreeNode import TypeTreeNode

DEV=Path(__file__).resolve().parents[2]
LIVE=Path(r'E:\EscapeFromTarkov')
OUT=DEV/'CJ-SDK/Assets/Mods/WTT-Campaigns.Assets/Recovered'
class Generator(TypeTreeGenerator):
    def get_nodes_up(self,assembly,fullname):
        key=(assembly,fullname)
        if key not in self.cache:
            nodes=self.get_nodes(assembly.removesuffix('.dll'),fullname)
            for node in nodes:
                if node.m_Level==1 and node.m_Name=='m_Enabled':node.m_MetaFlag|=16384
            self.cache[key]=TypeTreeNode.from_list([TypeTreeNode(n.m_Level,n.m_Type,n.m_Name,0,0,m_MetaFlag=n.m_MetaFlag) for n in nodes])
        return self.cache[key]

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    g=Generator('2022.3.43f2')
    g.load_il2cpp((LIVE/'GameAssembly.dll').read_bytes(),(DEV/'1.0 Metadata/global-metadata.dat').read_bytes())
    scripts=next(iter(UnityPy.load(str(LIVE/'EscapeFromTarkov_Data/globalgamemanagers.assets')).files.values())).objects
    provenance=[]
    for level,roots in {44:[5113,6378],47:[58,82,155,312,357,466],48:[1976],49:[794,2761],50:[213,681],'sharedassets44.assets':[1471,3075]}.items():
        source=LIVE/'EscapeFromTarkov_Data'/(f'level{level}' if isinstance(level,int) else level)
        env=UnityPy.load(str(source))
        env.typetree_generator=g
        objects=next(f for f in env.files.values() if hasattr(f,'objects')).objects
        def visit(go):
            tree=go.read_typetree()
            result={'id':go.path_id,'name':tree['m_Name'],'active':tree['m_IsActive'],'components':[],'children':[]}
            for c in go.read().m_Component:
                obj=c.component.deref()
                if obj.type.name=='RectTransform':
                    rect=obj.read_typetree()
                    result['rect']=rect
                    for ptr in obj.read().m_Children:
                        result['children'].append(visit(ptr.read().m_GameObject.deref()))
                elif obj.type.name=='MonoBehaviour':
                    try:
                        script_file,script_id=struct.unpack_from('<iq',obj.get_raw_data(),16)
                        assert script_file==1,'Unexpected MonoScript source'
                        script=scripts[script_id].read()
                        fullname=(script.m_Namespace+'.' if script.m_Namespace else '')+script.m_ClassName
                        fields=obj.read_typetree(g.get_nodes_up(script.m_AssemblyName,fullname))
                        result['components'].append({'id':obj.path_id,'type':fullname,'fields':fields})
                    except Exception as ex:
                        result['components'].append({'id':obj.path_id,'error':str(ex)})
                elif obj.type.name!='Transform':
                    result['components'].append({'id':obj.path_id,'type':obj.type.name,'fields':obj.read_typetree()})
            return result
        for root in roots:
            data=visit(objects[root]);file=OUT/f'{source.stem}-{root}.json'
            file.write_text(json.dumps(data,indent=2),encoding='utf-8')
            print(file.name,data['name'])
        provenance.append({'source':str(source),'sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'roots':roots})
    (OUT/'provenance.json').write_text(json.dumps(provenance,indent=2))
if __name__=='__main__':main()
