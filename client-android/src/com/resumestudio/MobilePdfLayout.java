package com.resumestudio;
import com.tom_roush.pdfbox.text.TextPosition;
import java.util.*;

/** Uses a repeated vertical gutter to read two-column PDF text column-by-column. */
final class MobilePdfLayout {
    private record Gap(double left,double right,double y){}
    static String text(List<TextPosition> positions,double width,double height){
        var rows=new TreeMap<Double,List<TextPosition>>(Comparator.reverseOrder());
        for(var position:positions)rows.computeIfAbsent(-Math.round(position.getYDirAdj()*10)/10.0,key->new ArrayList<>()).add(position);
        List<Gap> gaps=new ArrayList<>();
        for(var entry:rows.entrySet()){var row=entry.getValue();row.sort(Comparator.comparingDouble(TextPosition::getXDirAdj));for(int i=1;i<row.size();i++){double left=row.get(i-1).getXDirAdj()+row.get(i-1).getWidthDirAdj(),right=row.get(i).getXDirAdj();if(right-left>width*.08&&left<width*.7&&right>width*.25)gaps.add(new Gap(left,right,entry.getKey()));}}
        double split=0,top=0;List<Gap> best=new ArrayList<>();
        for(var gap:gaps){double x=(gap.left+gap.right)/2;List<Gap> matches=new ArrayList<>();for(var candidate:gaps)if(candidate.left<x&&candidate.right>x)matches.add(candidate);if(matches.size()>best.size()){best=matches;split=x;}}
        boolean columns=best.size()>=Math.max(3,rows.size()*.25)&&best.stream().mapToDouble(Gap::y).max().orElse(0)-best.stream().mapToDouble(Gap::y).min().orElse(0)>height*.18;
        if(columns)top=best.stream().mapToDouble(Gap::y).max().orElse(0);
        StringBuilder result=new StringBuilder();
        if(!columns){for(var row:rows.values())line(row,result);}
        else {
            for(var row:rows.entrySet())if(row.getKey()>top+2)line(row.getValue(),result);
            for(int side=0;side<2;side++)for(var entry:rows.entrySet())if(entry.getKey()<=top+2){List<TextPosition> row=new ArrayList<>();for(var letter:entry.getValue())if((letter.getXDirAdj()<split)==(side==0))row.add(letter);line(row,result);}
        }
        return result.toString();
    }
    private static void line(List<TextPosition> row,StringBuilder result){for(int i=0;i<row.size();i++){var letter=row.get(i);if(i>0&&letter.getXDirAdj()-row.get(i-1).getXDirAdj()-row.get(i-1).getWidthDirAdj()>letter.getFontSizeInPt()*.35)result.append(' ');result.append(letter.getUnicode());}if(!row.isEmpty())result.append('\n');}
}
